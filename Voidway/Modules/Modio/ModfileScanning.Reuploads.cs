using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Serialization;
using DSharpPlus.Commands;
using DSharpPlus.Commands.ContextChecks;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Entities;
using DSharpPlus.Interactivity;
using DSharpPlus.Interactivity.Extensions;
using Modio.Filters;
using Modio.Models;
using Newtonsoft.Json;

namespace Voidway.Modules.Modio;

internal partial class ModfileScanning
{
    private static readonly Encoding TextEncoding = Encoding.UTF8;
    
    private static async Task<(int foundExistingBarcodes, int foundExistingHashes)> ScanZipForReuploadedMods(ZipArchive zip, Mod modData)
    {
        if (modData.SubmittedBy?.NameId is null)
        {
            Logger.Warn($"Can't check for reupload by a null user (on Mod {modData.NameId}, #ID{modData.Id})");
            return (0, 0);
        }
        
        var hashes = GetHashEntries(zip);
        
        List<(string hash, string originalBarcode)> foundExistingHashes = [];
        List<(string barcode, string originalUploader)> foundExistingBarcodes = [];

        foreach (var entry in hashes)
        {
            string palletBarcode = entry.Name.Replace(".hash", "").Replace("catalog_", "");
            if (PersistentData.values.barcodesToOriginalUploaders.TryGetValue(palletBarcode, out var originalUploader)
                && originalUploader != modData.SubmittedBy.NameId)
            {
                Logger.Put(
                    $"Reupload detection found that the barcode {palletBarcode} was already posted by {originalUploader} " +
                    $"and not the current uploader {modData.SubmittedBy.NameId} (Mod {modData.NameId}, #ID{modData.Id})");
                
                foundExistingBarcodes.Add((palletBarcode, originalUploader));
                
                continue;
            }
            
            Logger.Warn($"Checking hash for {palletBarcode} (Mod {modData.NameId}, #ID{modData.Id})");
            using MemoryStream hashStream = new((int)entry.Length);
            await using var stream = await entry.OpenAsync();
            await stream.CopyToAsync(hashStream);
            
            string hashStr = TextEncoding.GetString(hashStream.ToArray());
            
            if (PersistentData.values.hashesToOriginalBarcodes.TryGetValue(hashStr, out var originalBarcode)
                && originalBarcode != palletBarcode)
            {
                Logger.Put(
                    $"Reupload detection found that the hash {hashStr} was posted under the barcode {originalBarcode} " +
                    $"and not the current barcode {palletBarcode} (Mod {modData.NameId}, #ID{modData.Id})");
                
                foundExistingHashes.Add((hashStr, originalBarcode));
                continue;
            }
        }

        foreach ((string hash, string originalBarcode) in foundExistingHashes)
        {
            if (PersistentData.values.barcodesToOriginalUploaders.TryGetValue(originalBarcode, out var originalUploader)
                && originalUploader != modData.SubmittedBy.NameId)
            {
                Logger.Put(
                    $"Reupload chained a hash ({hash}) detection to a barcode ({originalBarcode}) to an original uploader ({originalUploader}) " +
                    $"that's different from the CURRENT uploader {modData.SubmittedBy.NameId} (Mod {modData.NameId}, #ID{modData.Id})");
                
                foundExistingBarcodes.Add(($"{hash} -> {originalBarcode}", originalUploader));
            }
        }

        if (foundExistingBarcodes.Count == 0)
        {
            Logger.Put($"Didn't find any existing barcodes from {foundExistingHashes.Count} hashes on Mod {modData.NameId}, #ID{modData.Id} by {modData.SubmittedBy.NameId}.", LogType.Debug);
            return (0, 0);
        }

        string desc = $"**Potential reupload detected**" +
                      $"\n- {string.Join("\n- ", foundExistingBarcodes.Select(tup => $"`{tup.barcode}` from `{tup.originalUploader}` on Mod.IO"))}";
        
        DiscordEmbedBuilder deb = new()
        {
            Author = new()
            {
                Url = modData.SubmittedBy?.ProfileUrl?.ToString(),
                Name = modData.SubmittedBy is not null ? $"{modData.SubmittedBy.Username} (ID: {modData.SubmittedBy.NameId})" : "??? (Mod.io API is fantastic and reliable)",
            },
            Description = desc,
            Title = $"{modData.Name} (ID: {modData.NameId})",
            Url = modData.ProfileUrl?.ToString()
        };
        
        int successCount = 0;
        int failureCount = 0;
        foreach (var channel in Channels)
        {
            try
            {
                await channel.SendMessageAsync(deb.Build());
                successCount++;
            }
            catch
            {
                failureCount++;
            }
        }
        Logger.Put($"Posted {successCount} ({failureCount} failed) messages alerting moderators of possible reupload");
        return (foundExistingBarcodes.Count, foundExistingHashes.Count);
    }

    private static ZipArchiveEntry[] GetHashEntries(ZipArchive zip)
    {
        var hashes = zip.Entries
            .Where(e => e.Name.EndsWith(".hash") && e.Length == 32)
            .ToArray();
        return hashes;
    }

    async Task<string> CatalogBarcodeAndHashesFromUser(uint userId, Action<string>? updateStrCallback = null)
    {
        if (ModioHelper.BonelabClient is null || bot.ModIO.Value is null)
            return "The Mod.IO API client isn't initialized.";

        string logTag = $"User #ID {userId}";
        User? userObj = null;
        var modSearch = ModioHelper.BonelabClient.Search(ModFilter.SubmitterId.Eq(userId));
        
        int totalNewBarcodes = 0;
        int totalNewHashes = 0;
        int modsScanned = 0;
        Action<string>? recallback = null;
        try
        {
            var modList = await modSearch.ToList();

            if (modList.Count == 0)
            {
                return "That user hasn't uploaded any mods.";
            }
            userObj = modList.Select(mod => mod.SubmittedBy).FirstOrDefault(u => u is not null);
            if (userObj is not null)
            {
                logTag = $"{userObj.Username} ({userObj.NameId} #ID {userObj.Id})";
            }
            
            if (updateStrCallback is not null)
            {
                recallback = (updateStr) =>
                {
                    const double SPCS_PER_UNDERSCORE_WIDTH = 2.2;
                    const int MAX_UNDERSCORES = 50;
                    
                    var body = Logger.ShowLastLinesOf(updateStr, 1750);
                    
                    double percentageComplete = modsScanned / (double)modList.Count;
                    int underscoreCount = (int)Math.Round(MAX_UNDERSCORES * percentageComplete);
                    int spaceCount = (int)Math.Round((MAX_UNDERSCORES - underscoreCount) * SPCS_PER_UNDERSCORE_WIDTH);
                    string progressBar = $"[{new string('=', underscoreCount)}>{new string(' ', spaceCount)}]";
                    string progressUpdate = $"{progressBar}\n*(Scanned **{modsScanned}** mods of **{modList.Count}** total)*";
                    
                    updateStrCallback($"{body}\n{progressUpdate}");
                };
            }
            
            foreach (var mod in modList)
            {
                var res = await CatalogBarcodeAndHashesFromMod(mod.Id, recallback);
                totalNewBarcodes += res.newBarcodes;
                totalNewHashes += res.newHashes;
                modsScanned++;
            }

            return $"Scanned all **{modList.Count}** mod(s) from {logTag}\n" +
                   $"Found **{totalNewBarcodes}** new barcode(s) and **{totalNewHashes}** new hash(es).";
        }
        catch (Exception ex)
        {
            Logger.Warn($"Exception while scanning {logTag} for hashes and barcodes.", ex);
            PersistentData.WritePersistentData();
            return $"**Exception** on {logTag} after {totalNewBarcodes} new barcodes and {totalNewHashes} new hashes: {ex}";
        }
        
        
    }
    
    static async Task<(string displayString, int newBarcodes, int newHashes)> CatalogBarcodeAndHashesFromMod(uint modId, Action<string>? updateStrCallback = null)
    {
        // ReSharper disable once InconsistentNaming
        const int MB_BYTES = 1024 * 1024;
        if (ModioHelper.BonelabClient is null)
            return ("The Mod.IO API client isn't initialized.", 0, 0);

        string logTag = $"Mod {modId}";
        StringBuilder accumulator = new();
        int newBarcodes = 0;
        int newHashes = 0;
        try
        {
            var modClient = ModioHelper.BonelabClient[modId];
            var modData = await modClient.Get();
            logTag = $"Mod '{modData.NameId}' (#ID {modData.Id})";

            int scanned = 0;
            int total = 0;
            await foreach (var modFile in modClient.Files.Search().ToEnumerable())
            {
                total++;
                
                if (PersistentData.values.modFilesInCatalog.Contains(modFile.Id))
                {
                    accumulator.AppendLine();
                    accumulator.AppendLine($"### File {total} ignored");
                    accumulator.AppendLine($"*Modfile ID {modFile.Id} already seen.*");
                    // don't proc callback, odds are there are a ton of already-seen files in a row. 
                    continue;
                }
                
                accumulator.AppendLine();
                accumulator.AppendLine($"### File {total} scan"); // separation between mod files
                var platformStrings = modFile.Platforms.Select(p => $"{p.Platform?.Value ?? "NoPlatform"} ({p.Status})");
                string fileLogTag = $"File {modFile.Id} (for {string.Join(", ", platformStrings)}) {modFile.Filename ?? "<No Filename>"}";
                
                if (modFile.FileSize > Config.values.modioMaxFilesize * MB_BYTES)
                {
                    double fileSizeMb = modFile.FileSize / (double)MB_BYTES;
                    Put($"File is larger than configured max (File {fileSizeMb:0.00}MB > Config {Config.values.modioMaxFilesize}MB) - {fileLogTag}");
                    continue;
                }
                
                if (modFile.Download is null)
                {
                    Put($"-# Null download on {fileLogTag}");
                    continue;
                }

                await using var zip = await GetZip(modFile.Download);

                if (zip is null)
                {
                    Put($"Null download on {fileLogTag}");
                    continue;
                }

                
                var fileResults = await CatalogBarcodeAndHashesInFile(zip, accumulator, fileLogTag, modData);
                newBarcodes += fileResults.newBarcodes;
                newHashes += fileResults.newHashes;
                updateStrCallback?.Invoke(accumulator.ToString());
                PersistentData.values.modFilesInCatalog.Add(modFile.Id);
                scanned++;
            }

            PersistentData.WritePersistentData();
            accumulator.AppendLine(); // increase separation
            Put($"Done! Scanned {scanned} of {total} total mod file(s), finding **{newBarcodes} new barcode(s)** & **{newHashes} new hash(es)** for {logTag}");
            return (accumulator.ToString(), newBarcodes, newHashes);
        }
        catch (Exception ex)
        {
            Logger.Warn($"Exception while scanning {logTag} for hashes and barcodes.", ex);
            Logger.Warn("Accumulator contents before error: " + accumulator);
            PersistentData.WritePersistentData();
            return ($"**Exception** on {logTag}: {ex}\nBut before that, completed info: {accumulator}", newBarcodes, newHashes);
        }
        
        void Put(string str)
        {
            Logger.Put(Formatter.Strip(str.Replace("-# ", "")), LogType.Debug);
            accumulator.AppendLine(str);
            updateStrCallback?.Invoke(accumulator.ToString());
        }
    }

    private static async Task<(int newBarcodes, int newHashes)> CatalogBarcodeAndHashesInFile(ZipArchive zip, StringBuilder accumulator, string fileLogTag, Mod modData)
    {
        int newHashes = 0;
        int newBarcodes = 0;
        string? submitterNameId = modData.SubmittedBy?.NameId;
        var hashes = GetHashEntries(zip);
        Put($"*Found {hashes.Length} hash(es) in {fileLogTag}*");
        
        foreach (var entry in hashes)
        {
            string palletBarcode = entry.Name.Replace(".hash", "").Replace("catalog_", "");

            using MemoryStream hashStream = new((int)entry.Length);
            await using var stream = await entry.OpenAsync();
            await stream.CopyToAsync(hashStream);
            
            var hashStr = TextEncoding.GetString(hashStream.ToArray());

            Put($"Found pallet barcode: **{palletBarcode}**, and its hash: **{hashStr}**");

            if (PersistentData.values.hashesToOriginalBarcodes.TryAdd(hashStr, palletBarcode))
            {
                Put($"Associated hash **{hashStr}** with barcode **{palletBarcode}**");
                newHashes++;
            }
            else
            {
                string currBarcodeAssoc = PersistentData.values.hashesToOriginalBarcodes[hashStr];
                if (currBarcodeAssoc != palletBarcode)
                    Put($"!!! Hash **{hashStr}** is associated w/ **{currBarcodeAssoc}** -- not **{palletBarcode}**");
                else
                    Put($"-# {hashStr} *is already associated with barcode* {palletBarcode}");
            }

            if (submitterNameId is null)
            {
                Put("**Can't check creator association, the submitter's NameID was null.**");
            }
            else if (PersistentData.values.barcodesToOriginalUploaders.TryAdd(palletBarcode, submitterNameId))
            {
                Put($"Associated barcode **{palletBarcode}** with uploader **{submitterNameId}**");
                newBarcodes++;
            }
            else
            {
                string currUploaderAssoc = PersistentData.values.barcodesToOriginalUploaders[palletBarcode];
                if (currUploaderAssoc != submitterNameId)
                    Put($"!!! Barcode **{palletBarcode}** is associated with uploader **{currUploaderAssoc}** -- not **{submitterNameId}**");
                else
                    Put($"-# {palletBarcode} *is already associated with mod.io uploader* {submitterNameId}");
            }
        }
        
        Put($"Found **{newBarcodes} new barcode(s)** and **{newHashes} new hash(es)** in {fileLogTag}");
        return (newBarcodes, newHashes);

        void Put(string str)
        {
            Logger.Put(Formatter.Strip(str.Replace("-# ", "")), LogType.Debug);
            accumulator.AppendLine(str);
        }
    }
}
using System.IO.Compression;
using System.Text;
using DSharpPlus.Entities;
using Modio.Models;

namespace Voidway.Modules.Modio;


internal partial class ModfileScanning
{
    private static readonly Encoding TextEncoding = Encoding.UTF8;
    
    private static async Task ScanZipForReuploadedMods(ZipArchive zip, Mod modData)
    {
        if (modData.SubmittedBy?.NameId is null)
        {
            Logger.Warn($"Can't check for reupload by a null user (on Mod {modData.NameId}, #ID{modData.Id})");
            return;
        }
        
        var hashes = zip.Entries
            .Where(e => e.Name.EndsWith(".hash") && e.Length == 32)
            .ToArray();

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
            
            Logger.Warn($"");
            using MemoryStream hashStream = new((int)entry.Length);
            await using var stream = await entry.OpenAsync();
            await stream.CopyToAsync(hashStream);
            
            string hashStr = "";
            try
            {
                hashStr = TextEncoding.GetString(hashStream.ToArray());
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to decode a hash from its bytes! Try going in manually! From mod {modData.NameId}, #ID{modData.Id}", ex);
                continue;
            }
            
            
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
            return;
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
    }
    
    // public record BarcodeCatalogJobStatus(
    //     int TotalMods,
    //     int TotalDownloads,
    //     int ProcessedMods,
    //     int ProcessedDownloads,
    //     int FoundHashes,
    //     int FoundBarcodes,
    //     int NewHashes,
    //     int NewBarcodes);
    // static async Task CatalogBarcodeAndHash(uint )
}
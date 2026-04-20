using System.ComponentModel;
using System.IO.Compression;
using System.Text.RegularExpressions;
using DSharpPlus.Commands;
using DSharpPlus.Commands.ContextChecks;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.Interactivity;
using DSharpPlus.Interactivity.Enums;
using DSharpPlus.Interactivity.EventHandling;
using DSharpPlus.Interactivity.Extensions;
using Modio;
using Modio.Filters;
using Modio.Models;
using File = Modio.Models.File;

namespace Voidway.Modules.ModIO;

[Command("modscanning")]
internal partial class ModfileScanning(Bot bot) : ModuleBase(bot)
{
    public static HashSet<uint> DontAnnounceThese = [];
    
    private static int MaxFilesizeBytes => Config.values.modioMaxFilesize * 1024 * 1024;
    private static readonly HttpClient DownloadClient = new HttpClient();
    private static readonly HashSet<DiscordChannel> Channels = [];
    private static readonly Dictionary<uint, DateTime> LastScanTimes = [];

    private static List<Regex> AutoflagRegexes
    {
        get
        {
            if (field.Count != 0) return field;
            
            // in case its intentional that there are no flags
            if (PersistentData.values.filenameFlagList.Count == 0)
                return [];

            foreach (var flagRegexStr in PersistentData.values.filenameFlagList)
            {
                field.Add(new Regex(flagRegexStr, RegexOptions.IgnoreCase));
            }

            return field;
        }
    } = [];


    protected override async Task FetchGuildResources()
    {
        if (bot.DiscordClient is null)
            return;

        Channels.Clear();
        
        foreach (var guildKvp in bot.DiscordClient.Guilds)
        {
            var cfg = ServerConfig.GetConfig(guildKvp.Key);

            if (cfg.malformedUploadChannel == 0)
                continue;

            var channel = await guildKvp.Value.GetChannelAsync(cfg.malformedUploadChannel);
            Channels.Add(channel);
        }
        
        Logger.Put($"Got {Channels.Count} channels to send malformed Mod.IO upload notifications to");
    }
    
    protected override Task InitOneShot(GuildDownloadCompletedEventArgs args)
    {
        ModioHelper.OnEvent += OnModEvent;
        return Task.CompletedTask;
    }

    private async Task OnModEvent(ModioEventArgs arg)
    {
        switch (arg.Event.EventType)
        {
            case ModEventType.MOD_AVAILABLE: // mod posting is SUPPOSED to modfile_changed event, but doesn't always
            case ModEventType.MODFILE_CHANGED:
                await ScanFile(arg);
                break;
            default:
                return;
        }
    }

    private async Task ScanFile(ModioEventArgs modioEventArgs)
    {
        const long MB_BYTES = 1024 * 1024;
        
        var modClient = modioEventArgs.ModsClient[modioEventArgs.Event.ModId];
        var modData = await modClient.Get();
        var file = await modClient.Files.Search(FileFilter.Id.Desc().Limit(1)).First();
        var download = file?.Download;
        
        // just make sure i dont NRE early
        if (file is null)
        {
            Logger.Warn($"Failed to get zip file to scan! The first file on {modData.NameId} was null");
            return;
        }
        if (download is null)
        {
            Logger.Warn($"Failed to get zip file to scan! The download link for first file on {modData.NameId} was null");
            return;
        }

        if (LastScanTimes.TryGetValue(modData.Id, out var lastScanTime) && lastScanTime + TimeSpan.FromMinutes(1) > DateTime.Now)
        {
            Logger.Warn($"Not scanning from {modioEventArgs.Event.EventType} event on {modData.NameId} (#ID {modData.Id}) --" +
                        $"there was already an event within the last 1 minute that's been(/being) responded to.");
            return;
        }
        LastScanTimes[modData.Id] = DateTime.Now;

        // early-out on easy checks before downloading
        if (file.VirusStatus == 1 && file.VirusPositive == 1)
        {
            await AnnounceHeuristicResult(modData, ModContentHeuristic.VirusFlagged);
            return;
        }

        if (file.FileSize > MaxFilesizeBytes)
        {
            Logger.Warn($"Mod is {Math.Round(file.FileSize / (double)MB_BYTES, 2)}MB. Unable to scan.");
            await AnnounceHeuristicResult(modData, ModContentHeuristic.FileTooLarge);
            return;
        }
        
        await using var zip = await GetZip(download);

        if (zip is null)
        {
            Logger.Put($"Didn't get a file, bailing on scanning mod {modData.LogTag()}.");
            return;
        }

        try
        {
            await ScanZipForFlaggedFilenames(zip, modData);
        }
        catch (Exception ex)
        {
            Logger.Warn($"Caught exception while scanning/announcing flagged filenames on {modData.LogTag()}", ex);
        }
        
        try
        {
            await ScanZipForHeuristics(zip, modData);
        }
        catch (Exception ex)
        {
            Logger.Warn($"Caught exception while scanning/announcing heuristics on {modData.LogTag()}", ex);
        }

        try
        {
            await ScanZipForReuploadedMods(zip, modData);
        }
        catch (Exception ex)
        {
            Logger.Warn($"Caught exception while scanning/announcing reuploads(?) on {modData.LogTag()}", ex);
        }

        try
        {
            if (modData.SubmittedBy?.NameId is not null
                && PersistentData.values.trustedModders.Contains(modData.SubmittedBy.NameId))
            {
                var catalogResults = await CatalogBarcodeAndHashesFromMod(modData.Id);
                Logger.Put(
                    $"Found {catalogResults.newBarcodes} new barcode(s) and {catalogResults.newHashes} new hash(es)" +
                    $"from scan of {modData.LogTag()} by trusted modder {modData.SubmittedBy.LogTag()}");
            }
        }
        catch (Exception ex)
        {
            Logger.Warn("Caught exception while cataloging modfiles' barcode(s) & hash(es)", ex);
        }
    }

    private static async Task<ZipArchive?> GetZip(Download download)
    {
        if (download.BinaryUrl is null)
            return null;
        var stream = await DownloadClient.GetStreamAsync(download.BinaryUrl);
        ZipArchive zip = new(stream);
        return zip;
    }
}
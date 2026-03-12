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
using Modio.Models;
using File = Modio.Models.File;

namespace Voidway.Modules.Modio;

[Command("modscanning")]
internal partial class ModfileScanning(Bot bot) : ModuleBase(bot)
{
    public static HashSet<uint> DontAnnounceThese = [];
    
    private static int MaxFilesizeBytes => Config.values.modioMaxFilesize * 1024 * 1024;
    private static readonly HttpClient DownloadClient = new HttpClient();
    private static readonly HashSet<DiscordChannel> Channels = [];

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
        ModioEvents.OnEvent += OnModEvent;
        return Task.CompletedTask;
    }

    private async Task OnModEvent(ModioEventArgs arg)
    {
        switch (arg.Event.EventType)
        {
            // case ModEventType.MOD_AVAILABLE: mod posting already fires a modfile_changed event
            case ModEventType.MODFILE_CHANGED:
                await ScanFiles(arg);
                break;
            default:
                return;
        }
    }

    private async Task ScanFiles(ModioEventArgs modioEventArgs)
    {
        const long MB_BYTES = 1024 * 1024;
        
        var modClient = modioEventArgs.ModsClient[modioEventArgs.Event.ModId];
        var modData = await modClient.Get();
        var file = await modClient.Files.Search().First();
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
            Logger.Put($"Didn't get a file, bailing on scanning mod {modData.Name} ({modData.NameId}, #ID {modData.Id}).");
            return;
        }
        
        await ScanZipForFlaggedFilenames(zip, modData);

        var heuristics = ClassifyZipContents(zip);
        
        if (heuristics.HasFlag(ModContentHeuristic.MarrowMod) || heuristics.HasFlag(ModContentHeuristic.MarrowReplacer))
        {
            return;
        }
        
        DontAnnounceThese.Add(modData.Id);
        await AnnounceHeuristicResult(modData, heuristics);
    }


    private static async Task AnnounceHeuristicResult(Mod modData, ModContentHeuristic filenameHeuristic)
    {
        DiscordEmbedBuilder deb = new()
        {
            Author = new()
            {
                Url = modData.SubmittedBy?.ProfileUrl?.ToString(),
                Name = modData.SubmittedBy is not null ? $"{modData.SubmittedBy.Username} (ID: {modData.SubmittedBy.NameId})" : "??? (Mod.io API is fantastic and reliable)",
            },
            Description = "Mod files has/have: " + filenameHeuristic.ToString(),
            Title = $"{modData.Name} (ID: {modData.NameId})",
            Url = modData.ProfileUrl?.ToString()
        };
        
        foreach (var channel in Channels)
        {
            await channel.SendMessageAsync(deb.Build());
        }
        Logger.Put($"Announced in {Channels.Count} channel(s) that mod {modData.Name} ({modData.NameId}, #ID {modData.Id}) contains: {filenameHeuristic}");
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
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using DSharpPlus.Commands;
using DSharpPlus.Commands.ArgumentModifiers;
using DSharpPlus.Commands.ContextChecks;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Entities;
using DSharpPlus.Interactivity;
using DSharpPlus.Interactivity.Extensions;
using Modio;
using Modio.Filters;
using Modio.Models;
using Voidway.Modules.ModIO.DataTypes;

namespace Voidway.Modules.ModIO;

partial class ModfileScanning
{
    public static List<Regex> EventFlagRegexes
    {
        get
        {
            if (field.Count != 0) return field;
            
            // in case its intentional that there are no flags
            if (PersistentData.values.eventFlagRegexes.Count == 0)
                return [];

            foreach (var flagRegexStr in PersistentData.values.eventFlagRegexes)
            {
                field.Add(new Regex(flagRegexStr, RegexOptions.IgnoreCase));
            }

            return field;
        }
    } = [];

    private static async Task ScanBundleAndAnnounce(ZipArchive zipFile, Mod? modData = null)
    {
        var results = await ScanBundle(zipFile, modData);
        var flags = results.GetUniqueFlags();

        if (flags.Count == 0)
            return;

        
    }
    
    private async Task AnnounceScanResult(Mod modData, BundleScanResults results)
    {
        var uniqueFlags = results.GetUniqueFlags();
        DiscordMessageBuilder dmb = new();
        DiscordEmbedBuilder deb = new()
        {
            Author = new()
            {
                Url = modData.SubmittedBy?.ProfileUrl?.ToString(),
                Name = modData.SubmittedBy is not null ? $"{modData.SubmittedBy.Username} (ID: {modData.SubmittedBy.NameId})" : "??? (Mod.io API is fantastic and reliable)",
            },
            Description = $"Mod file has raised {uniqueFlags.Count} unique flag(s):\n`{string.Join("`,\n`", uniqueFlags)}",
            Title = $"{modData.Name} (ID: {modData.NameId})",
            Url = modData.ProfileUrl?.ToString()
        };

        dmb.AddEmbed(deb);
        dmb.AddStringFile($"{modData.NameId}-all.txt", results.ToString());
        dmb.AddStringFile($"{modData.NameId}-flag.txt", results.GetFlagReport());
        int successCount = 0;
        foreach (var channel in Channels.Values)
        {
            try
            {
                await channel.SendMessageAsync(dmb);
                successCount++;
            }
            catch
            {
                // ignore
            }
        }
        Logger.Put($"Announced in {successCount} channel(s) that {modData.LogTag()} raised {uniqueFlags.Count} unique flag(s).");
    }
    
    private static async Task<BundleScanResults> ScanBundle(ZipArchive zipFile, Mod? modData = null)
    {
        long totalSize = zipFile.Entries.Sum(e => e.Length);
        
        AssetsManager manager = new();
        
        foreach (var file in zipFile.Entries)
        {
            if (file.Length > int.MaxValue)
            {
                Logger.Warn($"Skipping loading {file} because it's larger than the int limit. {modData.LogTag()}");
                continue;
            }

            // Can't "using" this, it'll be used later.
            var seekableStream = new MemoryStream();
            await using (var stream = await file.OpenAsync())
            {
                // Need to copy to a memorystream so AssetTools can seek, because DeflateStream doesn't support seeking.
                await stream.CopyToAsync(seekableStream);
            }
            
            try
            {
                // Try to load literally everything in case someone tries editing the catalog to load make a file
                // not ending in .bundle a part of a mod
                manager.LoadBundleFile(seekableStream, file.FullName);
                Logger.Put($"Loaded the {seekableStream.Length} byte bundle {file.FullName}...");
            }
            catch
            {
                // Don't care. Many things probably don't load properly.
            }
            
        }
        
        var sw = Stopwatch.StartNew();
        var results = BundleReading.VisitBundles(manager);

        sw.Stop();
        Logger.Put($"Scanned {results.bundleCount} bundles with {results.gameObjects.Count} GameObjects. Total time: {sw.ElapsedMilliseconds} ms");
        sw.Restart();
        var (flaggedObjects, flaggedCalls, flaggedArgs) = results.GetFlaggedData();
        var allFlags = results.GetUniqueFlags();
        sw.Stop();
        Logger.Put($"Found {flaggedObjects.Count()} flagged objects, {flaggedCalls.Count()} flagged calls, and {flaggedArgs.Count()} flagged arguments, for a total of {allFlags.Count} flags raised in {sw.ElapsedMilliseconds} ms {modData.LogTag()}");

        return results;
    }
    
    [Command("eventflag")]
    public class BundleFlagCommands
    {
        [Command("malscan")]
        [Description("Scans a zip file for potentially malicious behavior")]
        [RequireApplicationOwner]
        public async Task ManuallyScanZipBundles(SlashCommandContext ctx, DiscordAttachment file)
        {
            if (file.ProxyUrl is null)
            {
                await ctx.RespondAsync("Looks like Discord didn't want to give me a URL. Try once more or try again later?", true);
                return;
            }

            await ctx.DeferResponseAsync(true);
            
            var stream = await DownloadClient.GetStreamAsync(file.Url);
            await using ZipArchive zip = new(stream);
            
            var results = await ScanBundle(zip);
            var raisedFlags = results.GetUniqueFlags();

            var scanStr = results.ToString();
            
            var dirb = new DiscordInteractionResponseBuilder();
            if (raisedFlags.Count == 0)
                dirb.WithContent($"No flags raised");
            else
            {
                dirb.WithContent($"{raisedFlags.Count} unique flag(s) raised");
                
                dirb.AddStringFile($"{Path.GetFileNameWithoutExtension(file.FileName)}-flags.txt", results.GetFlagReport());
            }
            
            dirb.AddStringFile($"{Path.GetFileNameWithoutExtension(file.FileName)}-all.txt", scanStr);

            dirb.AsEphemeral();
            await ctx.FollowupAsync(dirb);
        }
        
        [Command("getflags"), Description("Get the list of RegExes that will trigger the bot to flag an upload")]
        [RequirePermissions([], [DiscordPermission.Administrator])]
        public async Task GetAutoflagList(SlashCommandContext ctx)
        {
            if (PersistentData.values.eventFlagRegexes.Count == 0)
            {
                await ctx.RespondAsync("https://tenor.com/view/peter-griffin-chris-balls-sus-peter-gif-4662424033555008061", true);
                return;
            }

            List<Page> pages = [];
            Page currPage = new("Page 1");
            for (var i = 0; i < PersistentData.values.eventFlagRegexes.Count; i++)
            {
                if (i != 0 && i % 10 == 0)
                {
                    pages.Add(currPage);
                    currPage = new Page($"Page {(i / 10) + 1}");
                }
                currPage.Content += $"\n`{PersistentData.values.eventFlagRegexes[i]}`";
            }

            pages.Add(currPage);
            await ctx.Interaction.SendPaginatedResponseAsync(true, ctx.User, pages);
            // await ctx.RespondAsync($"- `{string.Join("`\n- `", PersistentData.values.eventFlagRegexes)}`", true);
        }
        
        
        [Command("removeflag"), Description("Remove something from the list of RegExes that will trigger the bot to flag an upload")]
        [RequirePermissions([], [DiscordPermission.Administrator])]
        public async Task RemoveFromAutoflagList(SlashCommandContext ctx, [Description("Don't escape markdown formatting, just paste it as you would from a regex tester.")] string flagToRemove)
        {
            if (PersistentData.values.eventFlagRegexes.Count == 0)
            {
                await ctx.RespondAsync("https://tenor.com/view/peter-griffin-chris-balls-sus-peter-gif-4662424033555008061", true);
                return;
            }

            if (!PersistentData.values.eventFlagRegexes.Remove(flagToRemove))
            {
                await ctx.RespondAsync("Uh, that wasn't in there in the first place, so it's still not there... Mission accomplished?", true);
                return;
            }
            
            PersistentData.WritePersistentData();
            EventFlagRegexes.Clear();
            await ctx.RespondAsync($"Done! There's now {PersistentData.values.eventFlagRegexes.Count} entries in the flag list", true);
        }
        
        [Command("addflag"), Description("Add something to the list of RegExes that will trigger the bot to flag an upload")]
        [RequirePermissions([], [DiscordPermission.Administrator])]
        public async Task AddToAutoflagList(SlashCommandContext ctx, [Description("Don't escape markdown formatting, just paste it as you would from a regex tester.")] string flagToAdd)
        {
            if (PersistentData.values.eventFlagRegexes.Contains(flagToAdd))
            {
                await ctx.RespondAsync("Uh, that was already in there, so now it's still there... Mission accomplished?", true);
                return;
            }
            
            PersistentData.values.eventFlagRegexes.Add(flagToAdd);
            PersistentData.WritePersistentData();
            EventFlagRegexes.Clear();
            await ctx.RespondAsync($"Done! There's now {PersistentData.values.eventFlagRegexes.Count} entries in the flag list", true);
        }

        [Command("flagStringLength"), Description("Gets or sets the length beyond which things with that length will be flagged")]
        [RequirePermissions([], [DiscordPermission.Administrator])]
        public async Task GetOrSetFlagLength(SlashCommandContext ctx, [Description("Leave blank to view")] int? value = null)
        {
            if (value.HasValue)
            {
                PersistentData.values.bundleStringFlagThreshold = value.Value;
                PersistentData.WritePersistentData();
                await ctx.RespondAsync($"Got it! The bundle string length threshold is now set to {PersistentData.values.bundleStringFlagThreshold}");
            }
            else
            {
                await ctx.RespondAsync($"The bundle string length threshold is currently set to {PersistentData.values.bundleStringFlagThreshold}");
            }
        }

        [Command("retroactiveMalscan"), Description("Scans all mods from a given range. Status msg will appear in this channel.")]
        [RequireApplicationOwner]
        public async Task RetroactiveScan(SlashCommandContext ctx, DiscordChannel sendIn,
            [Description("Date/time string, ex: '05/01/2008 6:00:00AM +5:00' (offset optional)")] DateTimeOffset begin,
            [Description("Date/time string, ex: '05/01/2008 6:00:00AM +5:00' (offset optional)")] DateTimeOffset end)
        {
            if (begin > end)
            {
                (begin, end) = (end, begin);
            }

            if (ModioHelper.BonelabClient is null)
            {
                await ctx.RespondAsync("Sorry, the mod.io API client wasn't initialized. Let the operator know and try again.", true);
                return;
            }

            await ctx.DeferResponseAsync(false);

            var filter = ModEventFilter.DateAdded.GreaterOrEqual(begin.ToUnixTimeSeconds())
                .And(ModEventFilter.DateAdded.LessThan(end.ToUnixTimeSeconds()))
                .And(ModEventFilter.EventType.Eq(ModEventType.MOD_AVAILABLE));
            
            try
            {
                var events = await ModioHelper.BonelabClient.Mods.GetEvents(filter).ToList();
                string msgBase = $"Found {events.Count} event(s) in the specified time range.";
                DateTime lastEdited = DateTime.Now;
                var followupMsg = await ctx.FollowupAsync(msgBase);
                await Task.Delay(15 * 1000); // because it might be a large time span with a lot of pagination that could've fired off a lot of requests

                int okayMods = 0;
                int skippedDueToSeen = 0;
                int flaggedMods = 0;
                int skippedDueToSize = 0;
                int failedDownloads = 0;
                int skippedDueToRateLimit = 0;
                HashSet<uint> seenMods = [];
                for (var index = 0; index < events.Count; index++)
                {
                    var modEvent = events[index];

                    if (!seenMods.Add(modEvent.ModId))
                    {
                        skippedDueToSeen++;
                        continue; // seen already
                    }
                    
                    // wait 1 sec because I don't want mod.io to ban my ass
                    await Task.Delay(1000);
                    
                    Mod modData;
                    BundleScanResults results;
                    // the mod could have been removed by now so uh, try-catch it, lol
                    try
                    {
                        modData = await ModioHelper.BonelabClient.Mods[modEvent.ModId].Get();
                        var modfileClient = ModioHelper.BonelabClient.Mods[modEvent.ModId].Files;
                        var file = await modfileClient.Search(FileFilter.Id.Desc().Limit(1)).First();

                        if (file is not null && file.FileSize / 1024 / 1024 > Config.values.modioMaxFilesize)
                        {
                            skippedDueToSize++;
                            continue;
                        }

                        await using var downloadedZip = await GetZip(file?.Download);

                        if (downloadedZip is not null)
                        {
                            results = await ScanBundle(downloadedZip);
                        }
                        else
                        {
                            failedDownloads++;
                            continue;
                        }
                    }
                    catch (RateLimitExceededException rlex)
                    {
                        // retry it after waiting an extra minute, lol
                        followupMsg = await followupMsg.ModifyAsync(followupMsg.Content + "\n... waiting an extra minute because of a ratelimit...");
                        await Task.Delay(60 * 1000);
                        index--;
                        continue;
                    }
                    catch
                    {
                        // mod was likely deleted
                        failedDownloads++;
                        continue;
                    }

                    var flags = results.GetUniqueFlags();
                    if (flags.Count != 0)
                    {

                        var dmb = new DiscordMessageBuilder();
                        dmb.WithContent($"{modData.Name} @ {modData.ProfileUrl} raised {flags.Count} flag(s)!");
                        dmb.AddStringFile($"{modData.NameId}-all.txt", results.ToString());
                        dmb.AddStringFile($"{modData.NameId}-flags.txt", results.GetFlagReport());
                        flaggedMods++;
                        await sendIn.SendMessageAsync(dmb);
                    }
                    else
                        okayMods++;
                    
                    // wait an extra second because I still don't want mod.io to ban my ass
                    await Task.Delay(1000);
                    
                    if (lastEdited.AddSeconds(5) < DateTime.Now)
                    {
                        string newMsg = msgBase + $"\nNow checking event {index}...\n" +
                                        $"So far, found {flaggedMods} flagged mods vs {okayMods} okay mods\n" +
                                        $"Skipped {skippedDueToSize} oversize mods\n" +
                                        $"Skipped {skippedDueToSeen} events because they were on mods seen already\n" +
                                        $"{failedDownloads} downloads failed, probably due to the mod being removed";
                        followupMsg = await followupMsg.ModifyAsync(newMsg);
                        lastEdited = DateTime.Now;
                    }
                }

                await followupMsg.ModifyAsync(followupMsg.Content + "\nDone!");
            }
            catch (Exception ex)
            {
                Logger.Warn($"Exception while mass-scanning mods for dangerous content", ex);
                await ctx.Interaction.RespondOrAppend($"\nAn error occurred: ```\n{ex}\n```");
            }
            
        }
    }
}
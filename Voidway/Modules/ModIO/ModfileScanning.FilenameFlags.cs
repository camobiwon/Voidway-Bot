using System.ComponentModel;
using System.IO.Compression;
using DSharpPlus.Commands;
using DSharpPlus.Commands.ContextChecks;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Entities;
using DSharpPlus.Interactivity;
using DSharpPlus.Interactivity.Extensions;
using Modio.Models;

namespace Voidway.Modules.ModIO;

internal partial class ModfileScanning
{
    private async Task ScanZipForFlaggedFilenames(ZipArchive zip, Mod modData)
    {
        List<string> flaggedFilenames = [];
        
        foreach (var zipEntry in zip.Entries)
        {
            string displayName = zipEntry.FullName;
            bool flagged = false;
            foreach (var regex in AutoflagRegexes)
            {
                if (regex.IsMatch(zipEntry.FullName))
                {
                    displayName = regex.Replace(displayName, match => $"**{match.Value}**");
                    flagged = true;
                }
            }

            if (flagged)
                flaggedFilenames.Add(displayName);
        }

        if (flaggedFilenames.Count != 0)
        {
            DontAnnounceThese.Add(modData.Id);
            await AnnounceFlaggedFiles(modData, flaggedFilenames);
        }
    }

    private async Task AnnounceFlaggedFiles(Mod modData, List<string> flaggedFilenames)
    {
        string desc = $"Flagged for...\n- {Logger.EnsureShorterThan(string.Join("\n- ", flaggedFilenames), 3950)}";
        
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
        foreach (var channel in Channels.Values)
        {
            try
            {
                await channel.SendMessageAsync(deb.Build());
                successCount++;
            }
            catch
            {
                // ignore
            }
        }
        
        Logger.Put($"Announced in {successCount} channel(s) that mod {modData.Name} ({modData.NameId}, #ID {modData.Id}) was flagged for:\n{string.Join(", ", flaggedFilenames)}", LogType.Normal, false);
    }
    
    
    [Command("getflags"), Description("Get the list of RegExes that will trigger the bot to flag an upload")]
    [RequirePermissions([], [DiscordPermission.Administrator])]
    public async Task GetAutoflagList(SlashCommandContext ctx)
    {
        if (PersistentData.values.filenameFlagList.Count == 0)
        {
            await ctx.RespondAsync("https://tenor.com/view/peter-griffin-chris-balls-sus-peter-gif-4662424033555008061", true);
            return;
        }

        List<Page> pages = [];
        Page currPage = new("Page 1");
        for (var i = 0; i < PersistentData.values.filenameFlagList.Count; i++)
        {
            if (i != 0 && i % 10 == 0)
            {
                pages.Add(currPage);
                currPage = new Page($"Page {(i / 10) + 1}");
            }
            currPage.Content += $"\n`{PersistentData.values.filenameFlagList[i]}`";
        }

        pages.Add(currPage);
        await ctx.Interaction.SendPaginatedResponseAsync(true, ctx.User, pages);
        // await ctx.RespondAsync($"- `{string.Join("`\n- `", PersistentData.values.filenameFlagList)}`", true);
    }
    
    
    [Command("removeflag"), Description("Remove something from the list of RegExes that will trigger the bot to flag an upload")]
    [RequirePermissions([], [DiscordPermission.Administrator])]
    public async Task RemoveFromAutoflagList(SlashCommandContext ctx, [Description("Don't escape markdown formatting, just paste it as you would from a regex tester.")] string flagToRemove)
    {
        if (PersistentData.values.filenameFlagList.Count == 0)
        {
            await ctx.RespondAsync("https://tenor.com/view/peter-griffin-chris-balls-sus-peter-gif-4662424033555008061", true);
            return;
        }

        if (!PersistentData.values.filenameFlagList.Remove(flagToRemove))
        {
            await ctx.RespondAsync("Uh, that wasn't in there in the first place, so it's still not there... Mission accomplished?", true);
            return;
        }
        
        PersistentData.WritePersistentData();
        AutoflagRegexes.Clear();
        await ctx.RespondAsync($"Done! Here's the new flag list:\n- `{string.Join("`\n- `", PersistentData.values.filenameFlagList)}`", true);
    }
    
    [Command("addflag"), Description("Add something to the list of RegExes that will trigger the bot to flag an upload")]
    [RequirePermissions([], [DiscordPermission.Administrator])]
    public async Task AddToAutoflagList(SlashCommandContext ctx, [Description("Don't escape markdown formatting, just paste it as you would from a regex tester.")] string flagToAdd)
    {
        if (PersistentData.values.filenameFlagList.Contains(flagToAdd))
        {
            await ctx.RespondAsync("Uh, that was already in there, so now it's still there... Mission accomplished?", true);
            return;
        }
        
        PersistentData.values.filenameFlagList.Add(flagToAdd);
        PersistentData.WritePersistentData();
        AutoflagRegexes.Clear();
        await ctx.RespondAsync($"Done! Here's the new flag list:\n- `{string.Join("`\n- `", PersistentData.values.filenameFlagList)}`", true);
    }
}
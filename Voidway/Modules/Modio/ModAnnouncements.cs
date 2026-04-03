using System.Text;
using System.Text.RegularExpressions;
using DSharpPlus.Commands;
using DSharpPlus.Commands.ContextChecks;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Modio.Models;

namespace Voidway.Modules.Modio;

[Flags]
public enum ModUploadType // use bitshift operator to act as a bitfield
{
    Unknown = 0,
    Avatar = 1 << 0,
    Level = 1 << 1,
    Spawnable = 1 << 2,
    Utility = 1 << 3,
}

[Command("modposting")]
internal class ModAnnouncements(Bot bot) : ModuleBase(bot)
{
    public static readonly Dictionary<uint, List<DiscordMessage>> announcedMods = [];

    private static readonly Regex LinkAndRequestParameter = new(@"(https:\/\/.+)(\?\w+)?");
    
    private static readonly ModUploadType[] ModUploadTypes = Enum.GetValues<ModUploadType>();
    private static readonly string[] ModUploadTypeNames = Enum.GetNames(typeof(ModUploadType));

    private static readonly Dictionary<ModUploadType, List<DiscordChannel>> announcementChannels = new()
    {
        { ModUploadType.Avatar, [] },
        { ModUploadType.Level, [] },
        { ModUploadType.Spawnable, [] },
        { ModUploadType.Utility, [] }
    };
    
    
    static ModUploadType IdentifyFromTags(IEnumerable<Tag> tags)
    {
        ModUploadType ret = ModUploadType.Unknown;
        foreach (Tag tag in tags)
        {
            if (string.IsNullOrEmpty(tag.Name)) continue; // ignore empty tags

            int uploadTypeIndex = ModUploadTypeNames.IndexOf(tag.Name);

            if (uploadTypeIndex != -1)
                ret |= ModUploadTypes[uploadTypeIndex]; // support the use of Flags by using bitwise operations
        }
        return ret;
    }

    protected override async Task InitOneShot(GuildDownloadCompletedEventArgs args)
    {
        ModioHelper.OnEvent += OnModioEvent;
    }

    private async Task OnModioEvent(ModioEventArgs args)
    {
        try
        {
            switch (args.Event.EventType)
            {
                case ModEventType.MOD_AVAILABLE:
                    // give uploader an extra minute to make thumbnail changes or something
                    _ = Task.Delay(60 * 1000).ContinueWith((_) => AnnounceMod(args));
                    break;
                case ModEventType.MOD_EDITED:
                    await UpdateAnnouncement(args.Event.ModId);
                    break;
                case ModEventType.MOD_DELETED:
                case ModEventType.MOD_UNAVAILABLE:
                    await UnannounceMod(args.Event.ModId);
                    break;
                default:
                    return; // ignore
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"Exception while dispatching a '{args.Event.EventType}' event for a mod with the number id {args.Event.ModId}", ex);
        }
    }

    private static async Task AnnounceMod(ModioEventArgs args)
    {
        var modClient = args.ModsClient[args.Event.ModId];
        var modData = await modClient.Get();
        var uploadType = IdentifyFromTags(modData.Tags);
        List<DiscordMessage> messageList = [];
        
        // duplicate announcement checks
        if (announcedMods.TryGetValue(modData.Id, out var oldMessageList) && oldMessageList.Count != 0)
        {
            Logger.Put($"Mod {modData.Name} ({modData.NameId}, #ID {modData.Id}) was already announced! Ignoring! (found via #ID)");
            return;
        }
        if (modData.NameId is not null && announcedMods.ContainsKey(unchecked((uint)modData.NameId.GetHashCode())))
        {
            Logger.Put($"Mod {modData.Name} ({modData.NameId}, #ID {modData.Id}) was already announced! Ignoring! (found via NameID hashcode)");
            return;
        }
        announcedMods[modData.Id] = messageList;
        if (modData.NameId is not null)
            announcedMods[unchecked((uint)modData.NameId.GetHashCode())] = messageList;
        
        // Spam checks
        bool isSpamProbably = false;
        if (Config.values.modioTagSpamThreshold != -1 && modData.Tags.Count >= Config.values.modioTagSpamThreshold)
        {
            Logger.Put($"Mod {modData.Name} ({modData.NameId}, #ID {modData.Id}) has {modData.Tags.Count} tags, at/over the {Config.values.modioTagSpamThreshold} threshold to be considered tagspam. Not announcing!");
            isSpamProbably = true;
            // return;
        }
        if (uploadType == ModUploadType.Unknown)
        {
            Logger.Warn($"Mod {modData.Name} ({modData.NameId}, #ID {modData.Id}) was not recognized (from tags) as any actual mod.");
            isSpamProbably = true;
            // return;
        }
        
        // scanned mod checks
        if (ModfileScanning.DontAnnounceThese.Contains(modData.Id))
        {
            Logger.Put($"Mod {modData.Name} ({modData.NameId}, #ID {modData.Id}) was already scanned and is designated to not be announced.");
            return;
        }

        var desc = modData.DescriptionPlaintext ?? modData.Description ?? "";
        var title = modData.Name ?? modData.NameId ?? "";
        string[] tags = modData.Tags.Select(tag => tag.Name ?? "").ToArray();
        bool hasCensoredContent = false;

        foreach (string censorItem in Config.values.dontAnnounceModsWith)
        {
            if (tags.Any(t => t.Contains(censorItem, StringComparison.InvariantCultureIgnoreCase)))
            {
                var highlightedTags = tags.Select(t => t.Replace(censorItem, $">> {censorItem.ToUpper()} <<",
                    StringComparison.InvariantCultureIgnoreCase));
                Logger.Put(
                    $"Tags on mod {modData.Name} ({modData.NameId}, #ID {modData.Id}) contain '{censorItem}' -- censoring!" +
                    $"(Tags: {string.Join(", ", highlightedTags)}");
                hasCensoredContent = true;
                break;
            }

            if (desc.Contains(censorItem, StringComparison.InvariantCultureIgnoreCase))
            {

                Logger.Put(
                    $"Description for mod {modData.Name} ({modData.NameId}, #ID {modData.Id}) contains '{censorItem}' -- censoring!" +
                    $"{desc.Replace(censorItem, $">> {censorItem.ToUpper()} <<", StringComparison.InvariantCultureIgnoreCase)}");
                hasCensoredContent = true;
                break;
            }
            
            if (title.Contains(censorItem, StringComparison.InvariantCultureIgnoreCase))
            {

                Logger.Put(
                    $"Title for mod {modData.Name} ({modData.NameId}, #ID {modData.Id}) contains '{censorItem}' -- censoring!" +
                    $"{title.Replace(censorItem, $">> {censorItem.ToUpper()} <<", StringComparison.InvariantCultureIgnoreCase)}");
                hasCensoredContent = true;
                break;
            }
        }

        if (modData.MaturityOption.HasFlag(MaturityOption.Explicit))
        {
            Logger.Put($"Mature flags ({modData.MaturityOption}) for mod {modData.Name} " +
                       $"({modData.NameId}, #ID {modData.Id}) show explicit content -- censoring!");
            hasCensoredContent = true;
        }
        
        string modName = (modData.Name ?? modData.NameId ?? "").Replace("&amp;", "&");
        string authorText = modData.SubmittedBy?.Username is not null
            ? $" created by **{modData.SubmittedBy.Username.Replace("&amp;", "&")}**"
            : "";
        string? modUrl = modData.ProfileUrl?.ToString();
        string announcementText = $"**{modName}**{authorText}\n\n{modUrl}";
        var messageBuilder = new DiscordMessageBuilder()
            .WithAllowedMentions([])
            .WithContent(announcementText);
        
        int sentMessageCount = 0;
        foreach (var flag in ModUploadTypes)
        {
            if (flag == ModUploadType.Unknown)
                continue; // ignore unknown lol

            if (!uploadType.HasFlag(flag))
                continue;

            foreach (var channel in announcementChannels[flag])
            {
                var censorUploadsInThisServer = !channel.GuildId.HasValue || !ServerConfig.GetConfig(channel.GuildId.Value).dontCensorModUploads;
                if (hasCensoredContent || isSpamProbably)
                {
                    if (censorUploadsInThisServer)
                        continue;
                    
                    Logger.Put($"Announcing {modData.Name} ({modData.NameId}, #ID {modData.Id}) despite spam/censored content due to {channel.Guild.Name}'s cfg");
                }
                
                try
                {
                    var msg = await channel.SendMessageAsync(messageBuilder);
                    messageList.Add(msg);
                    sentMessageCount++;

                    await TryReact(msg, DiscordEmoji.FromUnicode("👍"), DiscordEmoji.FromUnicode("👎"));
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Exception while announcing mod {modName} (#ID {modData.Id}) to announcement channel {channel}", ex);
                }
            }
        }
        
        Logger.Put($"Sent {sentMessageCount} messages to announce {modData.NameId}");
        _ = Task.Delay(TimeSpan.FromMinutes(15)).ContinueWith(_ => UpdateAnnouncement(modData.Id));
        // untrack after a while because we do not need to care about shit past like, a day
        _ = Task.Delay(TimeSpan.FromDays(1)).ContinueWith(_ =>
        {
            announcedMods.Remove(modData.Id);
            if (modData.NameId is not null)
                announcedMods.Remove(unchecked((uint)modData.NameId.GetHashCode()));
        });
    }

    public static async Task UnannounceMod(uint modId)
    {
        if (!announcedMods.TryGetValue(modId, out var messageList) || messageList.Count == 0)
            return;

        Logger.Put("Unannouncing mod w/ #ID " + modId);
        int unannounceCount = 0;
        int failCount = 0;
        for (var index = messageList.Count - 1; index >= 0; index--)
        {
            var announcement = messageList[index];
            try
            {
                messageList.Remove(announcement);
                await announcement.DeleteAsync();
                unannounceCount++;
            }
            catch
            {
                failCount++;
                // dnc
            }
        }
        Logger.Put($"Unannounced mod w/ #ID {modId}. {unannounceCount} successful deletion(s) & {failCount} failed deletion(s)");
    }

    public static async Task UpdateAnnouncement(uint modId)
    {
        if (!announcedMods.TryGetValue(modId, out var messageList) || messageList.Count == 0)
            return;

        int successCount = 0;
        int failCount = 0;
        foreach (var msg in messageList)
        {
            if (msg.Channel is null)
                continue;
            try
            {
                DiscordMessage updatedMessage = await msg.Channel.GetMessageAsync(msg.Id);

                var updatedContent = LinkAndRequestParameter.Replace(updatedMessage.Content, LinkUpdater);
                await updatedMessage.ModifyAsync(updatedContent);
                successCount++;
            }
            catch
            {
                failCount++;
                // dnc
            }
        }
        
        Logger.Put($"Finished updating announcements for modio mod w/ #ID {modId} -- {successCount} successes, {failCount} failures.");
    }

    private static string LinkUpdater(Match match)
    {
        var uniqueMatches = match.Groups.Cast<Group>()
            .Select(g => g.ToString())
            .Distinct()
            .Where(str => !string.IsNullOrWhiteSpace(str))
            .ToArray();
        if (uniqueMatches.Length <= 1)
        {
            return match.Value + "?1";
        }
        else if (uniqueMatches.Length == 2)
        {
            string reqString = match.Groups[1].Value;
            if (!int.TryParse(reqString.TrimStart('?'), out var num))
            {
                Logger.Put($"Failed to parse number from second match group {reqString} (with ? removed from start) -- returning original full string {match.Value}");
                return match.Value;
            }

            string ret = $"{match.Groups[0].Value}?{num + 1}"; 
            Logger.Put($"Replacing for new embed {match} => {ret}");
            return ret; 
        }

        Logger.Put(
            $"Regex caught more than two groups, this was not intended!\nGroups: ['{string.Join("', '", match.Groups.Values.Select(g => g.Value))}']",
            LogType.Normal, false);
        return match.Value;
    }


    protected override async Task FetchGuildResources()
    {
        if (bot.DiscordClient is null)
            return;


        foreach (var channelList in announcementChannels.Values)
        {
            channelList.Clear();
        }

        foreach (var guildKvp in bot.DiscordClient.Guilds)
        {
            var cfg = ServerConfig.GetConfig(guildKvp.Key);
            ulong idSum = cfg.allModsChannel + cfg.avatarChannel + cfg.levelChannel + cfg.spawnableChanel + cfg.utilityChanel;
            if (idSum == 0)
                continue; // lol ez
            
            var allModsChannel = cfg.allModsChannel == 0
                ? null
                : await guildKvp.Value.GetChannelAsync(cfg.allModsChannel);
            if (allModsChannel is not null)
            {
                foreach (var channelList in announcementChannels.Values)
                {
                    channelList.Add(allModsChannel);
                }
            }

            if (cfg.avatarChannel != 0)
            {
                var channels = announcementChannels[ModUploadType.Avatar];
                channels.Add(await guildKvp.Value.GetChannelAsync(cfg.avatarChannel));
            }
            
            if (cfg.levelChannel != 0)
            {
                var channels = announcementChannels[ModUploadType.Level];
                channels.Add(await guildKvp.Value.GetChannelAsync(cfg.levelChannel));
            }
            
            if (cfg.spawnableChanel != 0)
            {
                var channels = announcementChannels[ModUploadType.Spawnable];
                channels.Add(await guildKvp.Value.GetChannelAsync(cfg.spawnableChanel));
            }
            
            if (cfg.utilityChanel != 0)
            {
                var channels = announcementChannels[ModUploadType.Utility];
                channels.Add(await guildKvp.Value.GetChannelAsync(cfg.utilityChanel));
            }
        }

        int totalChannels = announcementChannels.Sum(kvp => kvp.Value.Count);
        Logger.Put($"Got {totalChannels} channels for Mod.IO announcements");
        if (totalChannels == 0)
            return;
        
        foreach (var channelKvp in announcementChannels)
        {
            Logger.Put($" - {channelKvp.Key}: {channelKvp.Value.Count} channel(s)");
        }
    }
    
    [RequireApplicationOwner]
    [Command("announce")]
    public static async Task ForceAnnounceMod(SlashCommandContext ctx, long modNumberId = 0, string nameId = "")
    {
        if (modNumberId == 0 && string.IsNullOrEmpty(nameId))
        {
            await ctx.RespondAsync("Hey you need to give a number ID or a Name ID (ex. `mod-name-or-something`).", true);
            return;
        }
        
        uint modId = unchecked((uint)modNumberId);
        var clint = ModioHelper.ModsClient; 

        if (clint is null)
        {
            await ctx.RespondAsync("The Mod.IO API wasn't initialized. Ask the operator to double check the logs and make sure their API key (and OAuth2 token, if applicable) are valid", true);
            return;
        }

        if (modId == 0)
        {
            try
            {
                var mod = await clint.Search(global::Modio.Filters.ModFilter.NameId.Eq(nameId)).First();
                if (mod is null)
                    throw new NullReferenceException("Could not find a mod with Name ID " + nameId);
                
                modId = mod.Id;
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to find a mod with the NameID '{nameId}'", ex);
                await ctx.RespondAsync("Couldn't find a mod with that Name ID... Try grabbing it from the mod's URL?", true);
                return;
            }
        }

        // remove from the "don't announce" list because an owner said "announce it anyway"
        ModfileScanning.DontAnnounceThese.Remove(modId);

        // also checks for duplicate announcements
        if (announcedMods.Remove(modId, out var announcementMsgs))
        {
            var announceKvp = announcedMods.FirstOrDefault(kvp => kvp.Value == announcementMsgs);
            if (announceKvp.Value is not null)
            {
                announcedMods.Remove(announceKvp.Key);
            }
        }

        var eventData = new ModEvent()
        {
            DateAdded = DateTimeOffset.Now.ToUnixTimeSeconds(),
            EventType = ModEventType.MOD_AVAILABLE,
            ModId = modId,
            Id = modId,
            UserId = 0, // not used by my code, lol. therefore don't care
        };
        var args = new ModioEventArgs(clint, eventData);

        await ctx.DeferResponseAsync(true);
        await AnnounceMod(args);

        await ctx.FollowupAsync("Ok! Announced the mod!", true);
    }
}
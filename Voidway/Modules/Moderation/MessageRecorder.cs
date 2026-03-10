using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

namespace Voidway.Modules.Moderation;

public class MessageRecorder(Bot bot) : ModuleBase(bot)
{
    private static readonly Dictionary<DiscordGuild, DiscordChannel> logChannels = [];
    private static HttpClient downloadClient = new(); 

    protected override async Task FetchGuildResources()
    {
        if (bot.DiscordClient is null)
            return;
        
        logChannels.Clear();
        
        foreach (var guildKvp in bot.DiscordClient.Guilds)
        {
            var cfg = ServerConfig.GetConfig(guildKvp.Key);

            if (cfg.msgLogChannel == 0)
                continue;

            try
            {
                var channel = await guildKvp.Value.GetChannelAsync(cfg.msgLogChannel);
                logChannels[guildKvp.Value] = channel;
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to fetch channel w/ ID {cfg.msgLogChannel} from {guildKvp.Value}", ex);
            }
        }
    }

    protected override async Task MessageUpdated(DiscordClient client, MessageUpdatedEventArgs args)
    {
        if (!logChannels.TryGetValue(args.Guild, out var channel))
            return;

        // it would probably be an ouroboros to log things that happen in the log channel
        if (logChannels.Values.Contains(args.Channel))
            return;
        
        DiscordMessage msgAfter = args.Message;
        DiscordMessage? msgBefore = args.MessageBefore;
        
        if (msgBefore is not null && msgAfter.Content == msgBefore.Content)
            return; // no text content changed, so probably nothing we can log
        if (!msgAfter.IsEdited)
            return; // probably discord firing off an event for an attachment refresh 

        DiscordMessageBuilder msgBuilder = new DiscordMessageBuilder()
            .WithAllowedMentions([]);
        
        var mainEmbed = new DiscordEmbedBuilder()
            .WithTitle("Message Edited")
            .AddField("New Content", string.IsNullOrEmpty(msgAfter.Content) ? "`[No Content]`" : msgAfter.Content)
            .WithColor(DiscordColor.Gray);
        
        
        
        if (msgBefore is not null)
        {
            mainEmbed.AddField("Old content",  msgBefore.Content);
            AddMetadataFields(msgAfter, mainEmbed);
            
            if (msgAfter.Attachments.Count != msgBefore.Attachments.Count)
            {
                msgBuilder.AddEmbed(mainEmbed);
                await MirrorAttachments(msgBefore, msgBuilder);
            }
            else 
            {
                if (msgAfter.Attachments.Count > 0)
                    mainEmbed.AddField("Attachments", $"{msgAfter.Attachments.Count} unchanged", true);
                
                msgBuilder.AddEmbed(mainEmbed);
            }
        }
        else
        {
            mainEmbed.AddField("Old content",  "*Unknown -- original message not cached*");
            AddMetadataFields(msgAfter, mainEmbed);
            msgBuilder.AddEmbed(mainEmbed);
        }
        
        try
        {
            await channel.SendMessageAsync(msgBuilder);
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to log message edit in {args.Guild} (msg: {msgAfter})", ex);
        }
    }

    protected override async Task MessageDeleted(DiscordClient client, MessageDeletedEventArgs args)
    {
        if (!logChannels.TryGetValue(args.Guild, out var channel))
            return;
        DiscordMessage msg = args.Message;
        
        DiscordMessageBuilder msgBuilder = new DiscordMessageBuilder();
        var mainEmbed = Embedize(msg);
        mainEmbed.WithColor(DiscordColor.DarkRed)
                 .WithTitle("Message deleted");

        msgBuilder.AddEmbed(mainEmbed);

        await MirrorAttachments(msg, msgBuilder);

        try
        {
            await channel.SendMessageAsync(msgBuilder);
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to log message deletion in {args.Guild} (msg: {msg})", ex);
        }
    }

    private static DiscordEmbedBuilder Embedize(DiscordMessage msg)
    {
        DiscordEmbedBuilder mainEmbed = new();
        mainEmbed.AddField("Content", string.IsNullOrEmpty(msg.Content) ? "`[No Content]`" : msg.Content);
        
        return AddMetadataFields(msg, mainEmbed);
    }

    private static DiscordEmbedBuilder AddMetadataFields(DiscordMessage msg, DiscordEmbedBuilder mainEmbed)
    {
        mainEmbed.AddField("User", msg.Author is null ? "*Unknown*" : Formatter.Mention(msg.Author), true)
            .AddField("Channel", msg.Channel is null ? "*Unknown*" : Formatter.Mention(msg.Channel), true)
            .AddField("Original Time", Formatter.Timestamp(msg.Timestamp, TimestampFormat.ShortDateTime), true);

        string footer = $"Message ID: {msg.Id}";
        if (msg.Author is not null)
            footer += $"\nUser: {msg.Author.Username} ({msg.Author.Id})";
        mainEmbed.WithFooter(footer);
        return mainEmbed;
    }

    // Use after you've filled things out
    private static async Task MirrorAttachments(DiscordMessage from, DiscordMessageBuilder to)
    {
        if (from.Attachments.Count == 0)
            return;
        
        DiscordEmbedBuilder attachmentInfoEmbed = new();
        attachmentInfoEmbed.WithTitle("Attachments");
        List<DiscordAttachment> failedReattaches = [];
        List<DiscordAttachment> successfulReattaches = [];

        // just so the index can be used as a filename fallback lol
        for (var i = 0; i < from.Attachments.Count; i++)
        {
            var attachment = from.Attachments[i];
            // dont bother trying to reattach files larger than 8mb
            // it would hold up the log to do so and this isn't regular user-facing 
            if (attachment.FileSize > 8 * 1024 * 1024)
            {
                failedReattaches.Add(attachment);
            }
            
            var res = await downloadClient.GetAsync(attachment.ProxyUrl);
            if (!res.IsSuccessStatusCode)
            {
                // retry failed requests unproxied
                res = await downloadClient.GetAsync(attachment.Url);
            }

            // ok the file's actually gone
            if (!res.IsSuccessStatusCode)
            {
                failedReattaches.Add(attachment);
                continue;
            }

            await using var fileStream = await res.Content.ReadAsStreamAsync();

            to.AddFile("SPOILER_" + (attachment.FileName ?? $"file{i}{Path.GetExtension(attachment.Url)}"), fileStream, AddFileOptions.CopyStream | AddFileOptions.CopyStream);
            successfulReattaches.Add(attachment);
        }

        attachmentInfoEmbed
            .AddField("Reattached files", string.Join("\n", failedReattaches.Select(a => a.Url)))
            .AddField("Linked attachments", string.Join("\n", successfulReattaches.Select(a => a.Url)));
        to.AddEmbed(attachmentInfoEmbed);
    }
}
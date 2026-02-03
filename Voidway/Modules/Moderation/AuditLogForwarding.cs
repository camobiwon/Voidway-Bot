using DSharpPlus.Entities;
using DSharpPlus.Entities.AuditLogs;
using DSharpPlus.EventArgs;
using CircularBuffer;

namespace Voidway.Modules.Moderation;

public record AuditLogInfo(DiscordUser Initiator, DiscordAuditLogActionType Action, DateTime When);

public class ModerationLogOptions
{
    // Common options
    public required string Title { get; init; }
    public DiscordUser? Target { get; init; }
    public required DiscordUser UserResponsible { get; init; }
    public string? Reason { get; init; }

    public string? Description { get; init; }
    
    /// <summary>
    /// Will be beneath the "User", "Moderator", "Reason" row
    /// </summary>
    public (string, string)? ExtraField { get; init; }

    public DiscordColor Color { get; init; } = DiscordColor.Grayple;
    
    public Action<DiscordEmbedBuilder>? EmbedPostProcessor { get; init; }
    public Action<DiscordMessageBuilder>? BuilderPostProcessor { get; init; }
}

public class AuditLogForwarding(Bot bot) : ModuleBase(bot)
{
    public static CircularBuffer<AuditLogInfo> IgnoreThese = new(128);
    // public static CircularBuffer<AuditLogInfo> ActionsTakenOnBehalfOfModerator = new(128);
    private static readonly Dictionary<DiscordGuild, DiscordChannel> logChannels = [];

    protected override async Task FetchGuildResources()
    {
        if (bot.DiscordClient is null)
            return;
        
        logChannels.Clear();
        
        foreach (var guildKvp in bot.DiscordClient.Guilds)
        {
            var cfg = ServerConfig.GetConfig(guildKvp.Key);
            
            if (cfg.moderationLogChannel == 0)
                continue;

            try
            {
                var channel = await guildKvp.Value.GetChannelAsync(cfg.moderationLogChannel);
                logChannels[guildKvp.Value] = channel;
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to fetch channel w/ ID {cfg.moderationLogChannel} from {guildKvp.Value}", ex);
            }
        }
    }

    protected override async Task GuildAuditLogCreated(DiscordClient client, GuildAuditLogCreatedEventArgs args)
    {
        if (!logChannels.ContainsKey(args.Guild))
            return;

        var logEntry = args.AuditLogEntry;
            
        foreach (var ignoredInfo in IgnoreThese)
        {
            if (ignoredInfo.Action != logEntry.ActionType)
                continue;
            if ((logEntry.CreationTimestamp - ignoredInfo.When).TotalSeconds > 1)
                continue;
            if (ignoredInfo.Initiator != logEntry.UserResponsible)
                continue;
            
            Logger.Put($"Ignoring audit log action {logEntry.ActionType} by {ignoredInfo.Initiator} -- it was set to be ignored.");
        }

        switch (logEntry.ActionType)
        {
            case DiscordAuditLogActionType.MessageBulkDelete:
                DiscordAuditLogMessageEntry msgLog = (DiscordAuditLogMessageEntry)logEntry;
                await LogModerationAction(args.Guild, logEntry.UserResponsible as DiscordMember,
                    $"{msgLog.MessageCount} messages purged from {Formatter.Mention(msgLog.Channel)}", logEntry.Reason is not null ? $"Reason: {logEntry.Reason}" : null,
                    embedPostProcessor: deb => deb.WithColor(DiscordColor.Red));
                break;
            case DiscordAuditLogActionType.Ban:
                DiscordAuditLogBanEntry banLog = (DiscordAuditLogBanEntry)logEntry;
                await LogModerationAction(args.Guild, logEntry.UserResponsible as DiscordMember,
                    $"User banned", logEntry.Reason is not null ? $"Reason: {logEntry.Reason}" : null,
                    embedPostProcessor: deb => deb.WithColor(DiscordColor.Red));
        }
    }
    
    public static Task LogBan(DiscordGuild guild, DiscordMember moderator, DiscordMember bannedMember, string reason)
        => LogModerationAction(guild, moderator, "User banned", , bannedMember, );
    

    public static async Task LogModerationAction(DiscordGuild guild, ModerationLogOptions options)
    {
        if (!logChannels.TryGetValue(guild, out var logChannel))
            return;

        DiscordMessageBuilder dmb = new();
        DiscordEmbedBuilder deb = new();

        // Set title and (optionally) description
        deb.WithTitle(options.Title)
            .WithColor(options.Color);
        
        if (!string.IsNullOrEmpty(options.Description))
            deb.WithDescription(options.Description);

        // Create "User" "Moderator" "Reason" row
        if (options.Target is not null)
        {
            deb.AddField("User", Formatter.Mention(options.Target), true);
            deb.WithFooter($"User: {options.Target.Username} ({options.Target.Id})");
        }
        deb.AddField("Moderator", Formatter.Mention(options.UserResponsible), true);
        if (!string.IsNullOrWhiteSpace(options.Reason))
        {
            deb.AddField("Reason", options.Reason, true);
        }

        // add extra field if needed
        if (options.ExtraField.HasValue)
        {
            var fieldOpts = options.ExtraField.Value;
            deb.AddField(fieldOpts.Item1, fieldOpts.Item2, false);
        }
        
        options.EmbedPostProcessor?.Invoke(deb);
        dmb.AddEmbed(deb);
        options.BuilderPostProcessor?.Invoke(dmb);

    }
}
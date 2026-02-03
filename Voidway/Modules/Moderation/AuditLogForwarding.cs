using System.ComponentModel;
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
    public required DiscordUser? UserResponsible { get; init; }
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
    
    // Interaction buttons
    private const string BUTTON_DISMISS_ID_START = "void.modlog.dismiss.";
    private const string BUTTON_SENDREASON_ID_START = "void.modlog.sendreason.";
    private const string BUTTON_CUSTOMREASON_ID_START = "void.modlog.startinteraction.";
    private static readonly Dictionary<string, Func<ComponentInteractionCreatedEventArgs, Task>> ButtonFollowups = []; 

    protected override async Task ComponentInteractionCreated(DiscordClient client, ComponentInteractionCreatedEventArgs args)
    {
        // dispatch waiting interaction
        if (ButtonFollowups.TryGetValue(args.Id, out var followup))
        {
            await followup(args);
            return;
        }

        var dirb = new DiscordInteractionResponseBuilder()
            .AsEphemeral()
            .WithContent("Uh, there doesn't seem to be anything set up to handle that button press. Check with the dev?");
        await args.Interaction.CreateResponseAsync(DiscordInteractionResponseType.ChannelMessageWithSource, dirb);
    }

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

        ModerationLogOptions options;

        switch (logEntry.ActionType)
        {
            case DiscordAuditLogActionType.MessageBulkDelete:
                DiscordAuditLogMessageEntry msgLog = (DiscordAuditLogMessageEntry)logEntry;
                options = new()
                {
                    Title = "Messages purged",
                    UserResponsible = logEntry.UserResponsible,
                    Description = $"{msgLog.MessageCount} messages purged from {Formatter.Mention(msgLog.Channel)}",
                    Reason = msgLog.Reason,
                    Color = DiscordColor.Red,
                };
                
                await LogModerationAction(args.Guild, options);
                break;
            case DiscordAuditLogActionType.Ban:
                DiscordAuditLogBanEntry banLog = (DiscordAuditLogBanEntry)logEntry;
                
                bool userStillAccessible = false;
                try
                {
                    var user = await client.GetUserAsync(banLog.Target.Id, true);
                    userStillAccessible = true;
                }
                catch
                {
                    Logger.Put("Ignore the above D#+ log, just seeing if a banned user is still accessible (they're not)");
                }
                
                options = new()
                {
                    Title = "User banned",
                    UserResponsible = logEntry.UserResponsible,
                    Reason = banLog.Reason,
                    Color = DiscordColor.DarkRed,
                    BuilderPostProcessor = userStillAccessible ? dmb => AddMessageButtons(dmb, banLog.Id, banLog.Reason ?? "*No reason provided*") : null
                };
                
                await LogModerationAction(args.Guild, options);
                break;
            case DiscordAuditLogActionType.Kick:
                
        }
    }

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
        deb.AddField("Moderator", options.UserResponsible is null ? "*Unknown*" : Formatter.Mention(options.UserResponsible), true);
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

        await logChannel.SendMessageAsync(dmb);
    }

    public static void AddMessageButtons(DiscordMessageBuilder dmb, DiscordGuild guild, DiscordUser sendTo, ulong id, string actioned, string? autoReason)
    {
        bool hasAutoReason = !string.IsNullOrWhiteSpace(autoReason);
        var buttonAutoReason = new DiscordButtonComponent(
            DiscordButtonStyle.Primary,
            BUTTON_SENDREASON_ID_START + id.ToString(),
            hasAutoReason ? "Send warning (Audit log reason)" : "Send warning (None in log)");
        var buttonCraftReason = new DiscordButtonComponent(
            DiscordButtonStyle.Secondary, 
            BUTTON_CUSTOMREASON_ID_START + id.ToString(),
            "Send warning (Custom reason)"); 
        var buttonDismiss = new DiscordButtonComponent(
            DiscordButtonStyle.Danger, 
            BUTTON_DISMISS_ID_START + id.ToString(),
            "Dismiss");

        if (!hasAutoReason)
        {
            buttonAutoReason.Disable();
        }


        if (!hasAutoReason)
            return;
        ButtonFollowups[buttonAutoReason.CustomId] = async args =>
        {
            var _this = ButtonFollowups[buttonAutoReason.CustomId];
            try
            {

                var dwb = new DiscordWebhookBuilder();
                var dirb = new DiscordInteractionResponseBuilder()
                    .AsEphemeral(false)
                    .WithContent($"Creating DM channel with {sendTo.Username}...");
                await args.Interaction.CreateResponseAsync(DiscordInteractionResponseType.ChannelMessageWithSource, dirb);

                DiscordChannel dmChannel;
                try
                {
                    dmChannel = await sendTo.CreateDmChannelAsync();
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Failed to send message to {sendTo} @ creating DM channel", ex);
                    dwb.WithContent($"Failed to create DM channel with {sendTo.Username}");
                    await args.Interaction.EditOriginalResponseAsync(dwb);
                    return;
                }

                dwb.WithContent($"Created DM channel with {sendTo.Username}\nSending message...");
                await args.Interaction.EditOriginalResponseAsync(dwb);

                await dmChannel.SendMessageAsync(
                    $"The moderators in **{guild.Name}** have {actioned} you for the following reason:\n" +
                    $"\"{autoReason}\"\n" +
                    $"-# *This bot doesn't relay messages to the staff of that server. If you want clarification, message a moderator / admin in that server.*");

                dwb.WithContent($"Created DM channel with {sendTo.Username}\nSent message!\nCleaning up...");
                await args.Interaction.EditOriginalResponseAsync(dwb);

                // remove buttons for further invocations
                var builder = new DiscordMessageBuilder(args.Message);
                builder.ClearComponents();
                await args.Message.ModifyAsync(builder);

                dwb.WithContent(
                    $"-# *Sent {sendTo.Username} ({Formatter.Mention(sendTo)}) a message letting them know why they were {actioned}. Reason given: {autoReason}*");
                await args.Interaction.EditOriginalResponseAsync(dwb);
            }
            finally
            {

                // if (interactionInProgress)
                //     return;
            }
        };
        
        dmb.AddActionRowComponent(buttonAutoReason, buttonCraftReason, buttonDismiss);
    }
}
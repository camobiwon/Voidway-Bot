using DSharpPlus.Entities;
using DSharpPlus.Entities.AuditLogs;
using DSharpPlus.EventArgs;

namespace Voidway.Modules.Moderation;

public partial class AuditLogForwarding
{
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
                    BuilderPostProcessor = userStillAccessible ? dmb => AddMessageButtons(dmb, args.Guild, banLog.Target, banLog.Id, "banned", logEntry.Reason) : null
                };
                
                await LogModerationAction(args.Guild, options);
                break;
            case DiscordAuditLogActionType.Kick:
                
        }
    }
}
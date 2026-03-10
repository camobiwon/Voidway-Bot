using System.Runtime.CompilerServices;
using System.Text;
using CircularBuffer;
using DSharpPlus.Entities;
using DSharpPlus.Entities.AuditLogs;
using DSharpPlus.EventArgs;

namespace Voidway.Modules.Moderation;

//TODO: add ExtraField setting to bans, kicks, and mutes (ALSO add to command handlers)
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
            if ((logEntry.CreationTimestamp - ignoredInfo.When).TotalSeconds > 10)
                continue;
            if (ignoredInfo.Initiator != logEntry.UserResponsible)
                continue;
            
            Logger.Put($"Ignoring audit log action {logEntry.ActionType} by {ignoredInfo.Initiator} -- it was set to be ignored.");
            return;
        }

        ModerationLogOptions options;
        (string, string) moderationInfoField;

        switch (logEntry.ActionType)
        {
            case DiscordAuditLogActionType.MessageBulkDelete:
                DiscordAuditLogMessageEntry msgLog = (DiscordAuditLogMessageEntry)logEntry;
                // Should this be handled in MessageRecorder?
                options = new()
                {
                    Title = "Messages Purged",
                    UserResponsible = logEntry.UserResponsible,
                    Description = $"{msgLog.MessageCount} messages purged from {Formatter.Mention(msgLog.Channel)}",
                    Reason = msgLog.Reason,
                    Color = DiscordColor.Orange,
                };
                
                await LogModerationAction(args.Guild, options);
                break;
            case DiscordAuditLogActionType.Ban:
                DiscordAuditLogBanEntry banLog = (DiscordAuditLogBanEntry)logEntry;
                
                await LogActionAndProvideMessageOptions(client, args, banLog.Target, "banned");
                break;
            case DiscordAuditLogActionType.Kick:
                DiscordAuditLogKickEntry kickLog =  (DiscordAuditLogKickEntry)logEntry;
                
                await LogActionAndProvideMessageOptions(client, args, kickLog.Target, "kicked");
                break;
            
            // this kind of has to be handled or have a handlER from the member update event to keep track of whether they're timed out beforehand  
            case DiscordAuditLogActionType.MemberUpdate:
                DiscordAuditLogMemberUpdateEntry memberUpdateLog = (DiscordAuditLogMemberUpdateEntry)logEntry;
                if (memberUpdateLog.UserResponsible is null
                    || memberUpdateLog.UserResponsible == memberUpdateLog.Target)
                    return; // user did it to themselves
                
                #region Timeout handling
                
                bool timedOutBefore = memberUpdateLog.TimeoutChange.Before.HasValue
                                      && memberUpdateLog.TimeoutChange.Before.Value > DateTimeOffset.Now;
                bool timedOutAfter = memberUpdateLog.TimeoutChange.After.HasValue
                                      && memberUpdateLog.TimeoutChange.After.Value > DateTimeOffset.Now;
                

                bool newlyTimedOut = !timedOutBefore && timedOutAfter;
                bool timeoutChanged = timedOutBefore && timedOutAfter;
                bool timeoutRemoved = timedOutBefore && !timedOutAfter;
                
                DateTimeOffset? timeoutEnd = memberUpdateLog.Target.CommunicationDisabledUntil;
                string? desc = timeoutEnd.HasValue ? $"Ends in {Formatter.Timestamp(timeoutEnd.Value)}" : null;
                if (newlyTimedOut && timeoutEnd.HasValue)
                {
                    await LogActionAndProvideMessageOptions(client, args, memberUpdateLog.Target, "muted", color: DiscordColor.Yellow, desc: desc);
                    return;
                }

                if (timeoutChanged && timeoutEnd.HasValue)
                {
                    var timeoutBefore = memberUpdateLog.TimeoutChange.Before;
                    bool? timeoutLengthened = !timeoutBefore.HasValue
                        ? null
                        : timeoutBefore < timeoutEnd;
                    
                    var extraField =
                    (
                        "Moderation info",
                        ModerationTracker.GetObservationStringFor(memberUpdateLog.Target)
                    );

                    if (!timeoutLengthened.HasValue)
                    {
                        options = new()
                        {
                            Title = "User Mute Duration Changed",
                            Description = desc,
                            Target = memberUpdateLog.Target,
                            UserResponsible = logEntry.UserResponsible,
                            Reason = logEntry.Reason,
                            ExtraField =  extraField,
                            Color = DiscordColor.Gray,
                        };
                    }
                    else
                    {
                        options = new()
                        {
                            Title = $"User Mute Duration {(timeoutLengthened.Value ? "Increased" : "Decreased")}",
                            Description = desc,
                            Target = memberUpdateLog.Target,
                            UserResponsible = logEntry.UserResponsible,
                            Reason = logEntry.Reason,
                            ExtraField =  extraField,
                            Color = timeoutLengthened.Value ? DiscordColor.Orange : DiscordColor.Aquamarine,
                        };
                    }
                
                    await LogModerationAction(args.Guild, options);
                    return;
                }

                if (timeoutRemoved)
                {
                    options = new()
                    {
                        Title = "User Unmuted",
                        Target = memberUpdateLog.Target,
                        UserResponsible = logEntry.UserResponsible,
                        Reason = logEntry.Reason,
                        Color = DiscordColor.Green,
                    };

                    await LogModerationAction(args.Guild, options);
                    return;
                }
                
                #endregion Timeout handling

                #region Nickname handling
                
                bool nicknameChanged = memberUpdateLog.NicknameChange.Before != memberUpdateLog.NicknameChange.After;

                if (nicknameChanged)
                {
                    string? before = memberUpdateLog.NicknameChange.Before;
                    string? after = memberUpdateLog.NicknameChange.After;
                    
                    options = new()
                    {
                        Title = "User Nickname Changed",
                        Target = memberUpdateLog.Target,
                        UserResponsible = logEntry.UserResponsible,
                        Reason = logEntry.Reason,
                        Description = $"{(before is not null ? Formatter.Sanitize(before) : "`None`")} → {(after is not null ? Formatter.Sanitize(after) : "`None`")}",
                        Color = DiscordColor.Yellow,
                    };

                    await LogModerationAction(args.Guild, options);
                    return;
                }
                
                #endregion Nickname handling
                
                break;
            
            case DiscordAuditLogActionType.MemberRoleUpdate:
                memberUpdateLog = (DiscordAuditLogMemberUpdateEntry)logEntry;
                if (memberUpdateLog.UserResponsible is null ||
                    memberUpdateLog.Target == memberUpdateLog.UserResponsible)
                    return; // assigned their own role

                HashSet<DiscordRole> rolesThatMatter = [];
                foreach (var channel in args.Guild.Channels.Values)
                {
                    bool isPrivateChannel = channel.PermissionOverwrites.Any(o =>
                        o.Id == args.Guild.Id && o.Denied.HasPermission(DiscordPermission.ViewChannel));
                    
                    foreach (var overwrite in channel.PermissionOverwrites)
                    {
                        if (overwrite.Type != DiscordOverwriteType.Role)
                            continue;

                        if (overwrite.Id == args.Guild.Id)
                            continue; // @everyone isn't a role

                        if (isPrivateChannel && overwrite.Allowed.HasPermission(DiscordPermission.ViewChannel))
                            rolesThatMatter.Add(await overwrite.GetRoleAsync());
                        else if (overwrite.Allowed.HasPermission(DiscordPermission.ManageMessages))
                            rolesThatMatter.Add(await overwrite.GetRoleAsync());
                    }
                }

                foreach (var role in args.Guild.Roles.Values.Where(r =>
                             r.IsHoisted
                             || !string.IsNullOrEmpty(r.IconHash) 
                             || r.Permissions.HasPermission(DiscordPermission.ManageMessages))
                         )
                {
                    rolesThatMatter.Add(role);
                }
                
                

                var descSb = new StringBuilder();
                
                List<(bool, DiscordRole)> changedRolesThatMatter = [];
                foreach (var role in memberUpdateLog.AddedRoles ?? [])
                {
                    if (rolesThatMatter.Contains(role))
                    {
                        descSb.AppendLine($"+ {role}");
                        changedRolesThatMatter.Add((true, role));
                    }
                }

                foreach (var role in memberUpdateLog.RemovedRoles ?? [])
                {
                    if (rolesThatMatter.Contains(role))
                    {
                        descSb.AppendLine($"- {role}");
                        changedRolesThatMatter.Add((false, role));
                    }
                }

                if (changedRolesThatMatter.Count == 0)
                    return;

                if (changedRolesThatMatter.All(tup => tup.Item1))
                {
                    // only additions
                    
                    options = new()
                    {
                        Title = "User Roles Added",
                        Target = memberUpdateLog.Target,
                        UserResponsible = logEntry.UserResponsible,
                        Description = $"```diff\n{descSb}\n```",
                        Reason = logEntry.Reason,
                        Color = DiscordColor.Green,
                    };

                    await LogModerationAction(args.Guild, options);
                    break;
                }
                else if (changedRolesThatMatter.All(tup => !tup.Item1))
                {
                    // only removals
                    
                    options = new()
                    {
                        Title = "User Roles Removed",
                        Target = memberUpdateLog.Target,
                        UserResponsible = logEntry.UserResponsible,
                        Description = $"```diff\n{descSb}\n```",
                        Reason = logEntry.Reason,
                        Color = DiscordColor.IndianRed,
                    };

                    await LogModerationAction(args.Guild, options);
                    break;
                }

                options = new()
                {
                    Title = "User Roles Changed",
                    Target = memberUpdateLog.Target,
                    UserResponsible = logEntry.UserResponsible,
                    Description = $"```diff\n{descSb}\n```",
                    Reason = logEntry.Reason,
                    Color = DiscordColor.Cyan,
                };

                await LogModerationAction(args.Guild, options);
                break;
            
            case DiscordAuditLogActionType.Unban:
                banLog = (DiscordAuditLogBanEntry)logEntry;
                options = new()
                {
                    Title = "User Unbanned",
                    Target = banLog.Target,
                    UserResponsible = logEntry.UserResponsible,
                    Reason = logEntry.Reason,
                    Color = DiscordColor.Green,
                };

                await LogModerationAction(args.Guild, options);
                return;
        }
    }

    private static async Task LogActionAndProvideMessageOptions(DiscordClient client,
        GuildAuditLogCreatedEventArgs args, DiscordUser removedUser, string actioned,
        (string, string)? logExtraField = null, DiscordColor? color = null, string? desc = null)

    {
        DiscordAuditLogEntry logEntry = args.AuditLogEntry;
        ModerationLogOptions options;
        bool userStillAccessible = false;
        try
        {
            var user = await client.GetUserAsync(removedUser.Id, true);
            userStillAccessible = true;
        }
        catch
        {
            Logger.Put(
                $"Ignore the above D#+ log, just seeing if a {actioned} user ({removedUser}) is still accessible (they're not)");
        }

        logExtraField ??= ("Moderation info", ModerationTracker.GetObservationStringFor(args.Guild.Id, removedUser.Id));
        
        var capitalizedAction = actioned.Length > 1 ? char.ToUpper(actioned[0]) + actioned[1..] : actioned.ToUpper();
        options = new()
        {
            Title = $"User {capitalizedAction}",
            UserResponsible = logEntry.UserResponsible,
            Description = desc,
            Reason = logEntry.Reason,
            Color = color ?? DiscordColor.Red,
            ExtraField =  logExtraField,
            BuilderPostProcessor = userStillAccessible
                ? dmb => AddMessageButtons(dmb, args.Guild, removedUser, logEntry.Id, actioned, logEntry.Reason)
                : null
        };

        await LogModerationAction(args.Guild, options);
    }
}
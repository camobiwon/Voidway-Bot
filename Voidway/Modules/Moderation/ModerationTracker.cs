using DSharpPlus.Entities;
using DSharpPlus.Entities.AuditLogs;
using DSharpPlus.EventArgs;

namespace Voidway.Modules.Moderation;

public class ModerationTracker(Bot bot) : ModuleBase(bot)
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);
    private const int SAVE_PERIOD = 128; // Just felt like it
    private int periodicSaveCounter;
    
    protected override async Task MessageCreated(DiscordClient client, MessageCreatedEventArgs args)
    {
        if (args.Author.IsBot)
            return;

        if (!PersistentData.values.observedMessages.TryGetValue(args.Guild.Id, out var guildMessageCalendar))
        {
            guildMessageCalendar = [];
            PersistentData.values.observedMessages.Add(args.Guild.Id, guildMessageCalendar);
        }
        
        if (!guildMessageCalendar.TryGetValue(Today, out var userMessageCounts))
        {
            userMessageCounts = [];
            guildMessageCalendar[Today] = userMessageCounts;
        }

        if (!userMessageCounts.TryAdd(args.Author.Id, 1))
            userMessageCounts[args.Author.Id]++;
        
        periodicSaveCounter++;
        if (periodicSaveCounter > SAVE_PERIOD)
        {
            periodicSaveCounter = 0;
            CleanOldDays();
            PersistentData.WritePersistentData();
        }
    }

    protected override async Task GuildAuditLogCreated(DiscordClient client, GuildAuditLogCreatedEventArgs args)
    {
        DiscordUser? target = null;
        try
        {
            dynamic dynArgs = args;
            target = dynArgs.Target;
        }
        catch
        {
            // not a "target" event
        }

        if (target is null)
        {
            try
            {
                dynamic dynArgs = args;
                target = dynArgs.TargetUser; // there are logs that have a *targetuser* prop and not *target*, i guess
            }
            catch
            {
                // not a "target" event
            }
        }
        

        if ((target?.IsBot ?? true) || target == args.AuditLogEntry.UserResponsible)
            return;
        
        
        if (!PersistentData.values.moderationActions.TryGetValue(args.Guild.Id, out var guildActionCalendar))
        {
            guildActionCalendar = [];
            PersistentData.values.moderationActions.Add(args.Guild.Id, guildActionCalendar);
        }

        
        if (!guildActionCalendar.TryGetValue(Today, out var userActions))
        {
            userActions = [];
            guildActionCalendar[Today] = userActions;
        }

        Lazy<List<string>> actionList = new(() =>
        {
            if (userActions.TryGetValue(target.Id, out var ret))
                return ret;
            ret = [];
            userActions.Add(target.Id, ret);

            return ret;
        });
        
        if (args.AuditLogEntry.ActionType == DiscordAuditLogActionType.MemberUpdate
            && args.AuditLogEntry is DiscordAuditLogMemberUpdateEntry memberUpdateEntry)
        {
            // don't log timeout duration changes or unmutes, only log when someone is muted
            bool mutedBefore = memberUpdateEntry.TimeoutChange.Before.HasValue
                            && memberUpdateEntry.TimeoutChange.Before.Value > DateTime.Now;
            bool mutedAfter = memberUpdateEntry.TimeoutChange.After.HasValue
                               && memberUpdateEntry.TimeoutChange.After.Value > DateTime.Now;

            if (mutedBefore || !mutedAfter)
                return;
            
            actionList.Value.Add("Muted");
        }

        switch (args.AuditLogEntry.ActionType)
        {
            case DiscordAuditLogActionType.Kick:
                actionList.Value.Add("Kicked");
                break;
            case DiscordAuditLogActionType.Ban:
                // I'd be surprised if someone gets banned, unbanned, and then does something causing their moderation
                // history to be printed out again all within the span of 31 days
                actionList.Value.Add("Banned");
                break;
            case DiscordAuditLogActionType.Unban:
                actionList.Value.Add("Unbanned");
                break;
            case DiscordAuditLogActionType.AutoModerationBlockMessage:
                actionList.Value.Add("Msg automodded");
                break;
        }
        
        periodicSaveCounter++;
        if (periodicSaveCounter > SAVE_PERIOD)
        {
            periodicSaveCounter = 0;
            CleanOldDays();
            PersistentData.WritePersistentData();
        }
    }

    private static void CleanOldDays()
    {
        DateOnly removeLessThan = Today.AddDays(-31);
        foreach (ulong guildId in PersistentData.values.observedMessages.Keys)
        {
            var dict = PersistentData.values.observedMessages[guildId];
            var daysToRemove = dict
                .Keys
                .Where(d => d <= removeLessThan)
                .ToArray();

            foreach (DateOnly day in daysToRemove)
            {
                dict.Remove(day);
            }
        }
        
        foreach (ulong guildId in PersistentData.values.moderationActions.Keys)
        {
            var dict = PersistentData.values.moderationActions[guildId];
            var daysToRemove = dict
                .Keys
                .Where(d => d <= removeLessThan)
                .ToArray();

            foreach (DateOnly day in daysToRemove)
            {
                dict.Remove(day);
            }
        }
    }

    public static string GetObservationStringFor(DiscordMember member) => GetObservationStringFor(member.Guild.Id, member.Id);
    public static string GetObservationStringFor(ulong guildId, ulong userId)
    {
        CleanOldDays();

        int trackedDays = 0;

        int seenMessagesCount = 0;
        var seenMessagesInGuild = PersistentData.values.observedMessages.GetValueOrDefault(guildId);
        if (seenMessagesInGuild is not null)
        {
            foreach (var dict in seenMessagesInGuild.Values)
            {
                trackedDays++;
                seenMessagesCount += dict.GetValueOrDefault(userId);
            }
        }
        
        var seenActions = new Dictionary<string, int>();
        var seenActionsInGuild = PersistentData.values.moderationActions.GetValueOrDefault(guildId);
        if (seenActionsInGuild is not null)
        {
            foreach (var dict in seenActionsInGuild.Values)
            {
                var seenActionsOnDay = dict.GetValueOrDefault(userId) ?? [];
                foreach (var actionName in seenActionsOnDay)
                {
                    int seenActionCount = seenActions.GetValueOrDefault(actionName);
                    seenActionCount++;
                    seenActions[actionName] = seenActionCount;
                }
            }
        }

        string trackedDayStr = $"-# *Seen over the course of {trackedDays} of the last 31 days*";
        if (seenMessagesCount == 0 && seenActions.Count == 0)
            return $"No messages & no moderation history\n{trackedDayStr}";
        
        static string StringifyAction(KeyValuePair<string, int> kvp)
        {
            string xTimes = kvp.Value == 1 ? "**once**" : $"**{kvp.Value}** times";
            return $"**{kvp.Key}** {xTimes}.";
        }
        string moderationHistoryStr = seenActions.Count == 0
            ? "No moderation history"
            : string.Join('\n', seenActions.OrderBy(kvp => kvp.Key).Select(StringifyAction));

        string messageHistoryStr = seenMessagesCount switch
        {
            0 => "**No** messages",
            1 => "**One** message",
            _ => $"**{seenMessagesCount}** messages"
        };

        return $"{messageHistoryStr}\n{moderationHistoryStr}\n{trackedDayStr}";
    }
}
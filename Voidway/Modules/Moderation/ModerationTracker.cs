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

        if (!PersistentData.values.observedMessages.TryGetValue(args.Guild.Id, out var guildMessageCalendar)
        {
            guildMessageCalendar = [];
            PersistentData.values.observedMessages.Add(args.Guild.Id, guildMessageCalendar);
        }

        
        if (!guildMessageCalendar.TryGetValue(Today, out var userMessageCounts))
        {
            userMessageCounts = [];
            guildMessageCalendar[Today] = userMessageCounts;
        }

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

        if ((target?.IsBot ?? true) || target == args.AuditLogEntry.UserResponsible)
            return;
        
        
        if (!PersistentData.values.observedMessages.TryGetValue(args.Guild.Id, out var guildMessageCalendar)
        {
            guildMessageCalendar = [];
            PersistentData.values.observedMessages.Add(args.Guild.Id, guildMessageCalendar);
        }

        
        if (!guildMessageCalendar.TryGetValue(Today, out var userMessageCounts))
        {
            userMessageCounts = [];
            guildMessageCalendar[Today] = userMessageCounts;
        }
        
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
            
            
        }
        
        periodicSaveCounter++;
        if (periodicSaveCounter > SAVE_PERIOD)
        {
            periodicSaveCounter = 0;
            CleanOldDays();
            PersistentData.WritePersistentData();
        }
    }
}
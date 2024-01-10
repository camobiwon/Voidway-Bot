using DSharpPlus.Entities;
using DSharpPlus.Entities.AuditLogs;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Voidway_Bot
{
    internal class PersistentData
    {
        public static event Action? PersistentDataChanged;
        public static PersistentData values;
        private const string PD_PATH = "./persistentData.json";
        private static readonly Timer writerTimer;
        public const int TRACK_PAST_DAYS = 30;

        public static IReadOnlyList<DateOnly> GapDays => values.gapDays;
        public static IEnumerable<DateOnly> TrackedDays => Enumerable.Range(0, TRACK_PAST_DAYS).Select(num => Today.AddDays(-num)).Where(d => !GapDays.Contains(d));
        static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);
        [JsonIgnore]
        public List<DateOnly> gapDays = new();

        // server -> user -> day -> msgCount
        public Dictionary<ulong, Dictionary<ulong, Dictionary<DateOnly, ushort>>> observedMessages = new();
        // server -> user -> day of actions
        public Dictionary<ulong, Dictionary<ulong, Dictionary<DateTime, DiscordAuditLogActionType>>> moderationActions = new();

        static PersistentData()
        {
            Console.WriteLine("Initializing persistent data storage");
            if (!File.Exists(PD_PATH))
            {
                File.WriteAllText(PD_PATH, JsonConvert.SerializeObject(new PersistentData())); // mmm triple parenthesis, v nice
            }

            AppDomain.CurrentDomain.ProcessExit += (_, _) => WritePersistentData();


            writerTimer = new(_ => WritePersistentData(), null, 2500, 5 * 60 * 1000);

            ReadPersistentData();
            TrimOldMessages();
            TrimOldModerations();
            WritePersistentData();
            Logger.Put("Finished persistent data init");
        }

        public static void OutputRawJSON()
        {
            Console.WriteLine(File.ReadAllText(PD_PATH));
        }

        [MemberNotNull(nameof(values), nameof(GapDays))]
        public static void ReadPersistentData()
        {
            string configText = File.ReadAllText(PD_PATH);
            values = JsonConvert.DeserializeObject<PersistentData>(configText) ?? new PersistentData();

            values.gapDays ??= new();
            values.gapDays.Clear();

            DateOnly today = DateOnly.FromDateTime(DateTime.Now);
            DateOnly currDay = DateOnly.FromDateTime(DateTime.Now - TimeSpan.FromDays(TRACK_PAST_DAYS));
            while (currDay != today)
            {
                if (!values.observedMessages.Values.SelectMany(d => d.Values).SelectMany(d => d.Keys).Any(d => d == currDay))
                    values.gapDays.Add(currDay);

                currDay = currDay.AddDays(1);
            }
        }

        public static void WritePersistentData()
        { 
            try
            {
                File.WriteAllText(PD_PATH, JsonConvert.SerializeObject(values));
                PersistentDataChanged?.Invoke();
            }
            catch (Exception ex)
            {
                Logger.Warn("Exception while writing persistent data!", ex);
            }
        }

        public static void TrimOldMessages()
        {
            DateOnly oneMonthAgo = DateOnly.FromDateTime(DateTime.Now - TimeSpan.FromDays(TRACK_PAST_DAYS));
            int count = 0;
            foreach (Dictionary<DateOnly, ushort> calendarDict in values.observedMessages.Values.SelectMany(d => d.Values))
            {
                var oldDates = calendarDict.Keys.Where(date => oneMonthAgo > date);

                foreach (DateOnly oldDate in oldDates)
                {
                    calendarDict.Remove(oldDate);
                    count++;
                }
            }

            if (count != 0)
                Logger.Put($"Trimmed {count} old message counters from persistent data");
        }

        public static void TrimOldModerations()
        {
            DateTime oneMonthAgo = DateTime.Now - TimeSpan.FromDays(TRACK_PAST_DAYS);
            int count = 0;
            foreach (Dictionary<DateTime, DiscordAuditLogActionType> daysWhereUserWasModerated in values.moderationActions.Values.SelectMany(d => d.Values))
            {
                var oldActionTimes = daysWhereUserWasModerated.Keys.Where(date => oneMonthAgo > date);

                foreach (DateTime oldActionTime in oldActionTimes)
                {
                    daysWhereUserWasModerated.Remove(oldActionTime);
                    count++;
                }
            }

            if (count != 0)
                Logger.Put($"Trimmed {count} old moderations from persistent data");
        }

        public static string GetModerationInfoFor(ulong guild, ulong user)
        {
            RemoveOldGaps();
            TrimOldModerations();

            string msgStr = GetMessageInfoStr(guild, user);
            string modStr = GetModerationInfoStr(guild, user);
            return msgStr + "\n" + modStr + "\n" + $"*{GapDays.Count} gap/untracked day(s)*";
        }

        static string GetModerationInfoStr(ulong guild, ulong user)
        {
            if (!values.moderationActions.TryGetValue(guild, out var userDict))
                return "**No** moderation history";
            if (!userDict.TryGetValue(user, out var actionsDict) || actionsDict.Count == 0)
                return "**No** moderation history";


            var actionsGrouped = actionsDict.Values.Distinct().Select(actionType => GetActionStringFromDict(actionType, actionsDict));
            return string.Join('\n', actionsGrouped);

            static string GetActionStringFromDict(DiscordAuditLogActionType action, Dictionary<DateTime, DiscordAuditLogActionType> actionsDict)
            {
                string actionName = action switch
                {
                    DiscordAuditLogActionType.AutoModerationUserCommunicationDisabled => "Timed out",
                    DiscordAuditLogActionType.Kick => "Kicked",
                    DiscordAuditLogActionType.Ban => "Banned",
                    DiscordAuditLogActionType.MessageDelete => "Msg deleted",
                    _ => action.ToString()
                };

                int count = actionsDict.Values.Count(k => k == action);

                return $"{actionName} **{count}** time{(count == 1 ? "" : "s")}";
            }
        }

        static string GetMessageInfoStr(ulong guild, ulong user)
        {
            if (!values.observedMessages.TryGetValue(guild, out var userMsgDict))
                return"**No observed messages in past month**";
            if (!userMsgDict.TryGetValue(user, out var msgCountDict) || msgCountDict.Count == 0)
                return"**No observed messages in past month**";
            
            return$"**{msgCountDict.Values.Sum(u => u)}** observed messages in past month";
        }

        static void RemoveOldGaps()
        {
            DateOnly oneMonthAgo = DateOnly.FromDateTime(DateTime.Now - TimeSpan.FromDays(TRACK_PAST_DAYS));
            values.gapDays.RemoveAll(d => oneMonthAgo > d);
        }
    }
}

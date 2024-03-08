using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.Exceptions;
using DSharpPlus.Interactivity.Extensions;
using DSharpPlus.SlashCommands;
using DSharpPlus.SlashCommands.Attributes;
using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Voidway_Bot
{
    // info on making a slash command is here. why? i dunno. https://github.com/DSharpPlus/DSharpPlus/tree/master/DSharpPlus.SlashCommands
    internal class SlashCommands : ApplicationCommandModule
    {
        public enum TimeType
        {
            [ChoiceName("Seconds")]
            SECONDS,
            [ChoiceName("Minutes")]
            MINUTES,
            [ChoiceName("Hours")]
            HOURS,
            [ChoiceName("Days")]
            DAYS,
            [ChoiceName("Weeks")]
            WEEKS,
            //[ChoiceName("Months")]
            //MONTHS discord silently fails to apply a monthlong timeout
        }

        static Dictionary<string, VoidwayModerationData> moderationsPerformedByCommand = new();
        public static void AddModerationPerformedByCommand(string mangledReason, VoidwayModerationData moderationData)
        {
            moderationsPerformedByCommand[mangledReason] = moderationData;
        }
        public static bool WasByBotCommand(string? reason, out VoidwayModerationData moderationData)
        {
            if (string.IsNullOrEmpty(reason))
            {
                moderationData = new("*Unknown*", "*Unknown*", VoidwayModerationData.TargetNotificationStatus.UNKNOWN);
                return false;
            }

            if (moderationsPerformedByCommand.TryGetValue(reason, out moderationData!))
            {
                moderationsPerformedByCommand.Remove(reason);
                return true;
            }
            else return false;
        }

        // Technically, this is the exact same thing as Timeout, but it's got a new command entry because it has a more user-friendly description.
        [SlashCommand("retimeout", "Changes a user's timeout, and logs it with a reason.")]
        [SlashRequirePermissions(Permissions.ModerateMembers, false)]
        public async Task ChangeTimeout(
            InteractionContext ctx,
            [Option("user", "The currently timed-out user to change the timeout of")]
            DiscordUser _victim,
            [Option("count", "The number of minutes/hours/days/etc to make the new timeout")]
            double count,
            [Option("timeUnit", "The unit of time to apply the timeout in")]
            TimeType unit,
            [Option("reason", "Why this user's timeout is being changed")]
            string reason
            )
        {
            DiscordMember victim = (DiscordMember)_victim;
            DateTimeOffset until = OffsetFromTimeType(unit, count);
            // can't retime whats not yet timed
            DateTimeOffset? currTimeout = victim.CommunicationDisabledUntil;
            if (!currTimeout.HasValue || currTimeout < DateTime.Now)
            {
                await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, new DiscordInteractionResponseBuilder()
                {
                    IsEphemeral = true,
                    Content = $"That user isn't timed out yet! Try using /timeout on {victim.Username}#{victim.Discriminator} first."
                });
                return;
            }

            try
            {

                string ogReason = reason;
                reason = $"By {ctx.User.Username}: " + reason;
                moderationsPerformedByCommand[reason] = new(ogReason, ctx.User.Username, VoidwayModerationData.TargetNotificationStatus.NOT_APPLICABLE);
                await victim.TimeoutAsync(until, reason);

                await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, new DiscordInteractionResponseBuilder()
                {
                    IsEphemeral = true,
                    Content = $"Timed out {victim.Username}#{victim.Discriminator} until <t:{until.ToUnixTimeSeconds()}:f>"
                });

            }
            catch (DiscordException ex)
            {
                Logger.Warn($"Unable to apply timeout to {victim.Username}#{victim.Discriminator} ({victim.Id}) in {ctx.Guild.Name}. Details below:\n\t{ex}");
                await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, new DiscordInteractionResponseBuilder()
                {
                    IsEphemeral = true,
                    Content = $"Unable to time out {victim.Username}#{victim.Discriminator}, tell the bot owner to look for a {ex.GetType().FullName} in the logs."
                });
            }
            // NVM ON THIS -> Must handle moderation logging now because current Timeout handler doesn't handle timeout changes 
            //DiscordAuditLogEntry? logEntry = await Moderation.TryGetAuditLogEntry(ctx.Guild, dale => dale.Reason == reason && dale.UserResponsible == Bot.CurrUser);
            //await Moderation.ModerationEmbed(
            //    ctx.Guild,
            //    victim,
            //    $"Timeout Changed | Will now end <t:{until.ToUnixTimeSeconds()}:R>",
            //    logEntry,
            //    DiscordColor.Cyan,
            //    "Old end time",
            //    $"<t:{currTimeout.Value.ToUnixTimeSeconds()}:t>");
        }

        // this is technically redundant to discord's timeout dialogue, except that thing gives ZERO granular control.
        [SlashCommand("timeout", "Times out a user, optionally DMs them the reason why, and logs it with a reason.")]
        [SlashRequirePermissions(Permissions.ModerateMembers, false)]
        public async Task AddTimeout(
            InteractionContext ctx,
            [Option("user", "The user to time out")]
            DiscordUser _victim,
            [Option("count", "The number of minutes/hours/days/etc to time the user out for")]
            double count,
            [Option("timeUnit", "The unit of time to apply the timeout in")]
            TimeType unit,
            [Option("reason", "Why this user is being timed out")]
            string reason,
            [Option("notifyWithReason", "DMs the user telling them the exact reason why they were muted.")]
            bool notifyWithReasonImmediately = false
            )
        {
            DiscordMember victim = (DiscordMember)_victim;
            DateTimeOffset until = OffsetFromTimeType(unit, count);
            // dont timeout if already timed out
            if (victim.CommunicationDisabledUntil.HasValue && victim.CommunicationDisabledUntil > DateTime.Now)
            {
                await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, new DiscordInteractionResponseBuilder()
                {
                    IsEphemeral = true,
                    Content = $"{victim.Username}#{victim.Discriminator} is already timed out"
                });
                return;
            }

            try
            {
                if (notifyWithReasonImmediately)
                    await ctx.DeferAsync(true); // sending messages takes time - defer so we have enough time to DM and get back to the user

                string ogReason = reason;
                reason = $"By {ctx.User.Username}: " + reason;
                // this is so fugly LMFAO
                VoidwayModerationData.TargetNotificationStatus notifStatus =
                    notifyWithReasonImmediately ?
                        (
                            await Moderation.SendWarningMessage(victim, "muted", ogReason, ctx.Guild.Name)
                            ? VoidwayModerationData.TargetNotificationStatus.SUCCESS 
                            : VoidwayModerationData.TargetNotificationStatus.FAILURE
                        )
                        : VoidwayModerationData.TargetNotificationStatus.NOT_ATTEMPTED;

                moderationsPerformedByCommand[reason] = new(ogReason, ctx.User.Username, notifStatus);
                await victim.TimeoutAsync(until, reason);

                await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, new DiscordInteractionResponseBuilder()
                {
                    IsEphemeral = true,
                    Content = $"Timed out {victim.Username}#{victim.Discriminator} until <t:{until.ToUnixTimeSeconds()}:f>"
                });

            }
            catch (DiscordException ex)
            {
                Logger.Warn($"Unable to apply timeout to {victim.Username}#{victim.Discriminator} ({victim.Id}) in {ctx.Guild.Name}. Details below:\n\t{ex}");
                await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, new DiscordInteractionResponseBuilder()
                {
                    IsEphemeral = true,
                    Content = $"Unable to time out {victim.Username}#{victim.Discriminator}, tell the bot owner to look for a {ex.GetType().FullName} in the logs."
                });
            }
        }

        static DateTimeOffset OffsetFromTimeType(TimeType unit, double count)
        {
            DateTime now = DateTime.UtcNow;
            TimeSpan span = unit switch
            {
                TimeType.SECONDS => TimeSpan.FromSeconds(1),
                TimeType.MINUTES => TimeSpan.FromMinutes(1),
                TimeType.HOURS => TimeSpan.FromHours(1),
                TimeType.DAYS => TimeSpan.FromDays(1),
                TimeType.WEEKS => TimeSpan.FromDays(7),
                //TimeType.MONTHS => TimeSpan.FromDays(30), //31? idk man
                _ => throw new NotImplementedException($"The {nameof(TimeType)} unit '{unit}' is not (yet) represented by {nameof(SlashCommands)}.{nameof(OffsetFromTimeType)}"),
            };

            return now.Add(span * count);
        }

        [SlashCommand("voidwaykick", "Kicks a user, optionally DMs them the reason why, and logs it with a reason.")]
        [SlashRequirePermissions(Permissions.KickMembers, false)]
        public async Task Kick(
            InteractionContext ctx,
            [Option("user", "The user to kick")]
            DiscordUser _victim,
            [Option("reason", "Why this user is being kicked")]
            string reason,
            [Option("notifyWithReason", "DMs the user telling them the exact reason why they were kicked.")]
            bool notifyWithReasonImmediately = false
            )
        {
            DiscordMember victim = (DiscordMember)_victim;
            
            try
            {
                await ctx.DeferAsync(true); // sending messages takes time - defer so we have enough time to DM and get back to the user

                string ogReason = reason;
                reason = $"By {ctx.User.Username}: " + reason;
                // this is so fugly LMFAO
                VoidwayModerationData.TargetNotificationStatus notifStatus =
                    notifyWithReasonImmediately ?
                        (
                            await Moderation.SendWarningMessage(victim, "kicked", ogReason, ctx.Guild.Name)
                            ? VoidwayModerationData.TargetNotificationStatus.SUCCESS
                            : VoidwayModerationData.TargetNotificationStatus.FAILURE
                        )
                        : VoidwayModerationData.TargetNotificationStatus.NOT_ATTEMPTED;

                moderationsPerformedByCommand[reason] = new(ogReason, ctx.User.Username, notifStatus);
                await Task.Delay(1000);

                Logger.Put($"Kicking user {victim.Username} at the instruction of moderator {ctx.User.Username}. Reason sent to audit log will be: '{reason}'");
                await victim.RemoveAsync(reason);
                
                await ctx.FollowUpAsync(new DiscordFollowupMessageBuilder()
                {
                    IsEphemeral = true,
                    Content = $"Kicked {victim.Username}#{victim.Discriminator}"
                });
                
                Logger.Put($"User ({victim.Username}) was kicked successfully");
            }
            catch (DiscordException ex)
            {
                Logger.Warn($"Unable to kick {victim.Username}#{victim.Discriminator} ({victim.Id}) in {ctx.Guild.Name}. Details below:\n\t{ex}");
                await ctx.FollowUpAsync(new DiscordFollowupMessageBuilder()
                {
                    IsEphemeral = true,
                    Content = $"Unable to kick {victim.Username}#{victim.Discriminator}, tell the bot owner to look for a {ex.GetType().FullName} in the logs."
                });
            }
        }

        [SlashCommand("voidwayban", "Kicks a user, optionally DMs them the reason why, and logs it with a reason.")]
        [SlashRequirePermissions(Permissions.BanMembers, false)]
        public async Task Ban(
            InteractionContext ctx,
            [Option("user", "The user to ban")]
            DiscordUser _victim,
            [Option("deletemsgdays", "How many days back to purge their messages (0-7)")]
            long delDays,
            [Option("reason", "Why this user is being banned")]
            string reason,
            [Option("notifyWithReason", "DMs the user telling them the exact reason why they were banned.")]
            bool notifyWithReasonImmediately = false
            )
        {
            DiscordMember victim = (DiscordMember)_victim;

            try
            {
                await ctx.DeferAsync(true); // sending messages takes time - defer so we have enough time to DM and get back to the user

                string ogReason = reason;
                reason = $"By {ctx.User.Username}: " + reason;
                // this is so fugly LMFAO
                VoidwayModerationData.TargetNotificationStatus notifStatus =
                    notifyWithReasonImmediately ?
                        (
                            await Moderation.SendWarningMessage(victim, "banned", ogReason, ctx.Guild.Name)
                            ? VoidwayModerationData.TargetNotificationStatus.SUCCESS
                            : VoidwayModerationData.TargetNotificationStatus.FAILURE
                        )
                        : VoidwayModerationData.TargetNotificationStatus.NOT_ATTEMPTED;

                moderationsPerformedByCommand[reason] = new(ogReason, ctx.User.Username, notifStatus);
                await victim.BanAsync((int)Math.Clamp(delDays, 0, 7), reason);


                await ctx.FollowUpAsync(new DiscordFollowupMessageBuilder()
                {
                    IsEphemeral = true,
                    Content = $"Banned {victim.Username}#{victim.Discriminator}"
                });

            }
            catch (DiscordException ex)
            {
                Logger.Warn($"Unable to ban {victim.Username}#{victim.Discriminator} ({victim.Id}) in {ctx.Guild.Name}. Details below:\n\t{ex}");
                await ctx.FollowUpAsync(new DiscordFollowupMessageBuilder()
                {
                    IsEphemeral = true,
                    Content = $"Unable to ban {victim.Username}#{victim.Discriminator}, tell the bot owner to look for a {ex.GetType().FullName} in the logs."
                });
            }
        }

        [SlashCommand("testmodannouncement", "Allows an admin to test mod uploads")]
        [SlashCommandPermissions(Permissions.Administrator)]
        public async Task TestModAnnouncement(
            InteractionContext ctx,
            [Option("modid", "Mod.io mod ID")]
            long modId,
            [Option("userid", "Mod.io user ID")]
            long userId
            )
        {
            ModUploads.NotifyNewMod((uint)modId, (uint)userId);

            await ctx.CreateResponseAsync($"Tested mod uploads with mod ID {modId} and user ID {userId}", true);
        }

        [SlashCommand("dumpModerationHistory", "Dumps all moderation history a given user has within the past month.")]
        [SlashRequirePermissions(Permissions.ViewAuditLog, false)]
        public async Task DumpModerationInfoFor(
            InteractionContext ctx,
            [Option("userId", "The ID to search records for")]
            string userId = "",
            [Option("user", "The user whose ID to check records for")]
            DiscordUser? user = null,
            [Option("showResultsEphemeral", "Whether to send the results secretly (true) or send as a normal message (false)")]
            bool ephemeral = true
            )
        {
            ulong id;
            if (user is not null)
                id = user.Id;
            else if (ulong.TryParse(userId, out ulong parsedId))
                id = parsedId;
            else
            {
                await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, new() { Content = "You either need to provide a user's ID number or a user!", IsEphemeral = true });
                return;
            }

            string str = PersistentData.GetModerationInfoFor(ctx.Guild.Id, id);
            str = $"**Moderation history for <@{id}>**\n{str}";
            await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, new() { Content = str, IsEphemeral = ephemeral });
        }
    }
}

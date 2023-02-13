using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.Exceptions;
using DSharpPlus.Interactivity.Extensions;
using DSharpPlus.SlashCommands;
using DSharpPlus.SlashCommands.Attributes;
using Emzi0767.Utilities;
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

        static Dictionary<string, string> reasonsByBot = new();
        public static bool WasByBotCommand(string reason, out string user)
        {
            if (reasonsByBot.TryGetValue(reason, out user!))
            {
                reasonsByBot.Remove(reason);
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
                reason = $"By {ctx.User.Username}: " + reason;
                reasonsByBot[reason] = ctx.User.Username;
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
        [SlashCommand("timeout", "Times out a user, and logs it with a reason.")]
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
            string reason
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
                reason = $"By {ctx.User.Username}: " + reason;
                reasonsByBot[reason] = ctx.User.Username;
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
    }
}

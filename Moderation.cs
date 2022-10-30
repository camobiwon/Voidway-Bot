using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.Exceptions;

namespace Voidway_Bot {
	internal class Moderation {
		internal static void HandleModeration(DiscordClient discord) {
			discord.GuildMemberRemoved += (client, e) => KickHandler(e);
			discord.GuildMemberUpdated += (client, e) => { TimeoutHandler(e); return Task.CompletedTask; };
			discord.GuildMemberUpdated += (client, e) => HoistHandler(e.MemberAfter);
			discord.GuildMemberAdded += (client, e) => NewAccountHandler(e);
			discord.GuildMemberAdded += (client, e) => HoistHandler(e.Member);
            discord.GuildBanAdded += (client, e) => BanAddHandler(e);
			discord.GuildBanRemoved += (client, e) => BanRemoveHandler(e);
			discord.MessageDeleted += (client, e) => MessageEmbed(e.Guild, e.Message, "Deleted");
			discord.MessageUpdated += (client, e) => MessageEmbed(e.Guild, e.Message, "Edited", e.MessageBefore);
		}

		private static async void TimeoutHandler(GuildMemberUpdateEventArgs e) {
			// if the user's timeout status truly has not changed
			if (e.CommunicationDisabledUntilBefore == e.CommunicationDisabledUntilAfter)
				return;

			DateTime now = DateTime.UtcNow; // discord audit logs use UTC time
			DiscordAuditLogEntry? logEntry = await TryGetAuditLogEntry(e.Guild, dale => dale.ActionType == AuditLogActionType.MemberUpdate && FuzzyFilterByTime(dale, now));

			// means timeout changed
			if (e.CommunicationDisabledUntilBefore.HasValue && e.CommunicationDisabledUntilAfter.HasValue)
				await Moderation.ModerationEmbed( // wrap huge call chain
                e.Guild,
                e.Member,
                $"Timeout Changed | Will now end <t:{e.CommunicationDisabledUntilAfter.Value.ToUnixTimeSeconds()}:R>",
                logEntry,
                DiscordColor.Cyan,
                "Old end time",
                $"<t:{e.CommunicationDisabledUntilBefore.Value.ToUnixTimeSeconds()}:t>");
            else if (e.CommunicationDisabledUntilAfter.HasValue && e.CommunicationDisabledUntilAfter > DateTime.Now)
				await ModerationEmbed(e.Guild, e.Member, $"Timed Out. Ends <t:{e.CommunicationDisabledUntilAfter.Value.ToUnixTimeSeconds()}:R>", logEntry, DiscordColor.Yellow);
			else if (e.CommunicationDisabledUntilBefore.HasValue && !e.CommunicationDisabledUntilAfter.HasValue)
				await ModerationEmbed(e.Guild, e.Member, $"Timeout Removed", logEntry, DiscordColor.Cyan);
		}

		private static async Task KickHandler(GuildMemberRemoveEventArgs e) {
			DateTime now = DateTime.UtcNow;
			DiscordAuditLogEntry? kickEntry = await TryGetAuditLogEntry(e.Guild, dale => dale.ActionType == AuditLogActionType.Kick);
			Logger.Put($"{e.Guild.Name}, {e.Member.Username}, ke==null:{kickEntry is null}", Logger.Reason.Trace);
			if (kickEntry is null) return;
			if (!FuzzyFilterByTime(kickEntry, now, 10 * 1000))
			{
				Logger.Put($"User seems to have left, not been kicked (audit log kick entry is over 10sec old) {e.Member.Username}#{e.Member.Discriminator} ({e.Member.Id})");
				return;
			}

			await ModerationEmbed(e.Guild, e.Member, "Kicked", kickEntry, DiscordColor.Orange);
		}

		private static async Task NewAccountHandler(GuildMemberAddEventArgs e)
		{
			// used Math.Abs here because i couldnt be fucked to figure out the right subtraction order lol
			double daysOld = Math.Abs(e.Member.CreationTimestamp.Subtract(DateTime.UtcNow).TotalDays);
			double hoursBetweenCreationAndJoin = Math.Abs(e.Member.JoinedAt.Subtract(e.Member.CreationTimestamp).TotalHours);
			ulong logChannel = Config.FetchNewAccountLogChannel(e.Guild.Id);

			if (logChannel is not 0 && daysOld < 1 && hoursBetweenCreationAndJoin < 1)
				await e.Guild.Channels[logChannel].SendMessageAsync($"Member <@{e.Member.Id}> ({e.Member.Username}#{e.Member.Discriminator}) has a new account & joined recently after creation.");
		}

		private static async Task BanAddHandler(GuildBanAddEventArgs e)
		{
			DiscordAuditLogEntry? banEntry = await TryGetAuditLogEntry(e.Guild, dale => dale.ActionType == AuditLogActionType.Ban);
			await ModerationEmbed(e.Guild, e.Member, "Banned", banEntry, DiscordColor.Red);
		}

		private static async Task BanRemoveHandler(GuildBanRemoveEventArgs e)
		{
			DiscordAuditLogEntry? unbanEntry = await TryGetAuditLogEntry(e.Guild, dale => dale.ActionType == AuditLogActionType.Unban);
			await ModerationEmbed(e.Guild, e.Member, "Ban Removed", unbanEntry, DiscordColor.Green);
		}

		private static async Task HoistHandler(DiscordMember m)
		{
			bool isHoistServer = Config.IsHoistServer(m.Guild.Id);
			bool needsHoist = Config.IsHoistMember(m.DisplayName[0]);

			if (isHoistServer && needsHoist)
			{
				// try-catch in case an admin does some shit like "#CANADAFOREVER" (in which case they should not be admin because canada)
				try
				{
					await m.ModifyAsync(mem => { mem.Nickname = "hoist"; mem.AuditLogReason = $"Hoist: name started w/ {m.DisplayName[0]}"; });
					Logger.Put($"Hoisted {m.Username}#{m.Discriminator} ({m.Id}) for display name {m.DisplayName} in {m.Guild.Name}");
				}
				catch (DiscordException dex)
				{
					Logger.Warn($"Failed to hoist {m.Username}#{m.Discriminator} ({m.Id}) for display name {m.DisplayName} in {m.Guild.Name}\n\t" + dex.ToString());
				}
			}
		}

        private static Task ModerationEmbed(DiscordGuild guild, DiscordMember victim, string actionType, DiscordAuditLogEntry? logEntry, DiscordColor color, string customFieldTitle = "", string customField = "") {
			(string loggedMod, string reason) = GetDataFromReason(logEntry);
            DiscordEmbedBuilder embed = new() {
				Title = $"User {actionType}",
				Color = color,
				Footer = new DiscordEmbedBuilder.EmbedFooter() { Text = $"User ID: {victim.Id}" }
			};
			embed.AddField("User", $"{victim.Username}", true);
			embed.AddField("Moderator", $"{loggedMod}", true);
			if (!string.IsNullOrEmpty(reason))
				embed.AddField("Reason", reason, true);
			else if (!string.IsNullOrEmpty(customFieldTitle))
				embed.AddField(customFieldTitle, customField, true);

			if (logEntry is null) Logger.Put("Moderation embed created with nonexistent log entry! See below.", Logger.Reason.Warn);
			Logger.Put($"{actionType} {victim.Username}#{victim.Discriminator} ({victim.Id}) in '{guild.Name}' by {loggedMod} (Audit log says: {logEntry?.UserResponsible.Username ?? "Unknown"})");
			guild.GetChannel(Config.FetchModerationChannel(guild.Id)).SendMessageAsync(embed);
			return Task.CompletedTask;
		}

		private static Task MessageEmbed(DiscordGuild guild, DiscordMessage message, string actionType, DiscordMessage? pastMessage = null) {
			Task.Delay(1000);
			if (message.Author is null)
				return Task.CompletedTask;

			if (message.Author.IsBot)
				return Task.CompletedTask;

			if (pastMessage != null && pastMessage.Content == message.Content)
				return Task.CompletedTask;

			string attachments = "";
			foreach (DiscordAttachment attachment in message.Attachments)
				attachments += $"\n{attachment.Url}";

			DiscordEmbedBuilder embed = new() {
				Title = $"Message {actionType}",
				Color = DiscordColor.Gray,
				Description = $"{(!string.IsNullOrEmpty(message.Content) ? "**Message**\n" + message.Content : "")} {(pastMessage != null ? "\n\n**Past Message**\n" + pastMessage.Content : (!string.IsNullOrEmpty(attachments) ? "\n\n**Attachments**" + attachments : ""))}",
				Footer = new DiscordEmbedBuilder.EmbedFooter() { Text = $"User ID: {message.Author.Id} | Message ID: {message.Id}" }
			};
			embed.AddField("User", $"{message.Author.Username}", true);
			embed.AddField("Channel", $"<#{message.Channel.Id}>", true);
			embed.AddField("Original Time", $"<t:{message.Timestamp.ToUnixTimeSeconds()}:f>", true);

			guild.GetChannel(Config.FetchMessagesChannel(guild.Id)).SendMessageAsync(embed);
			return Task.CompletedTask;
		}


		private static async Task<DiscordAuditLogEntry?> TryGetAuditLogEntry(DiscordGuild guild, Func<DiscordAuditLogEntry, bool> predicate)
		{
			//AuditLogActionType? alat = (AuditLogActionType?)auditLogType;
			//await Task.Delay(250);

			for (int i = 1; i < Config.GetAuditLogRetryCount() + 1; i++)
			{

				foreach (DiscordAuditLogEntry auditLogEntry in await guild.GetAuditLogsAsync(i/*, null, alat*/))
				{
					if (predicate(auditLogEntry)) return auditLogEntry;
				}

				await Task.Delay(1000 * i); // progressively increase delay to avoid setting off ratelimiting
			}

			return null;
		}


		private static bool FuzzyFilterByTime(DiscordAuditLogEntry logEntry, DateTime currTime, int fuzzByMs = 5000)
		{
			//TimeSpan fuzzBy = TimeSpan.FromMilliseconds(fuzzByMs);
			int msDiff = (int)Math.Abs(logEntry.CreationTimestamp.DateTime.Subtract(currTime).TotalMilliseconds);
			bool ret = msDiff < fuzzByMs;
			// might be better to make a VS2022 debug logpoint but oh well
			Logger.Put($"logentry creation: {logEntry.CreationTimestamp.DateTime:h:mm:ss:t}; time: {currTime:h:mm:ss:t}; diff={msDiff}ms; fuzzby: {fuzzByMs}ms; RET={ret}", Logger.Reason.Debug);
			return ret;
		}

		private static (string, string) GetDataFromReason(DiscordAuditLogEntry? logEntry)
		{
			DiscordUser? userResponsible = logEntry?.UserResponsible;
			if (logEntry is null || userResponsible is null) return ("*Unknown*", logEntry?.Reason ?? "");
			else if (userResponsible != Bot.CurrUser) return (userResponsible.Username, logEntry.Reason);

			// now handle the case that the bot took action

			if (SlashCommands.WasByBotCommand(logEntry.Reason, out string userResp)) {
				int userResponsibleColonCount = userResp.Count(c => c == ':'); // in case someone's name is some shit like "Cheese grater 2: Eclectic Shitfuck"
                string cleanReason = string.Join(':', logEntry.Reason.Split(':').Skip(1 + userResponsibleColonCount));
                return (userResp, cleanReason);
			}
			else return (userResponsible.Username, logEntry.Reason);
        }
	}
}

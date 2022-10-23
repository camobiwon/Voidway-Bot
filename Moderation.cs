using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

namespace Voidway_Bot {
	internal class Moderation {
		internal static void HandleModeration(DiscordClient discord) {
			discord.GuildMemberRemoved += (client, e) => KickHandler(e);
			discord.GuildMemberUpdated += (client, e) => { TimeoutHandler(e); return Task.CompletedTask; };
			discord.GuildBanAdded += (client, e) => BanAddHandler(e);
			discord.GuildBanRemoved += (client, e) => BanRemoveHandler(e);
			discord.MessageDeleted += (client, e) => MessageEmbed(e.Guild, e.Message, "Deleted");
			discord.MessageUpdated += (client, e) => MessageEmbed(e.Guild, e.Message, "Edited", e.MessageBefore);
		}

		private static async void TimeoutHandler(GuildMemberUpdateEventArgs e) {
			// if the user's timeout status hasnt changed
			if (e.CommunicationDisabledUntilBefore.HasValue == e.CommunicationDisabledUntilAfter.HasValue) return;

			DateTime now = DateTime.UtcNow; // discord audit logs use UTC time
			DiscordAuditLogEntry? logEntry = await TryGetAuditLogEntry(e.Guild, dale => dale.ActionType == AuditLogActionType.MemberUpdate && FuzzyFilterByTime(dale, now));

			if(e.CommunicationDisabledUntilAfter.HasValue && e.CommunicationDisabledUntilAfter > DateTime.Now)
				await ModerationEmbed(e.Guild, e.Member, $"Timed Out", logEntry, DiscordColor.Yellow, "Until", $"<t:{e.CommunicationDisabledUntilAfter.Value.ToUnixTimeSeconds()}:f>");
			else if(e.CommunicationDisabledUntilBefore.HasValue && !e.CommunicationDisabledUntilAfter.HasValue)
				await ModerationEmbed(e.Guild, e.Member, $"Timeout Removed", logEntry, DiscordColor.Gray);
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

        private static Task ModerationEmbed(DiscordGuild guild, DiscordMember victim, string actionType, DiscordAuditLogEntry? logEntry, DiscordColor color, string customFieldTitle = "", string customField = "") {
			DiscordEmbedBuilder embed = new() {
				Title = $"User {actionType}",
				Color = color,
				Footer = new DiscordEmbedBuilder.EmbedFooter() { Text = $"User ID: {victim.Id}" }
			};
			embed.AddField("User", $"{victim.Username}", true);
			embed.AddField("Moderator", $"{logEntry?.UserResponsible.Username ?? "*Unknown*"}", true);
			if(!string.IsNullOrEmpty(logEntry?.Reason))
				embed.AddField("Reason", logEntry.Reason, true);
			else if(!string.IsNullOrEmpty(customFieldTitle))
				embed.AddField(customFieldTitle, customField, true);

			if (logEntry is null) Logger.Put("Moderation embed created with nonexistent log entry! See below.", Logger.Reason.Warn);
			Logger.Put($"{actionType} {victim.Username}#{victim.Discriminator} ({victim.Id}) in '{guild.Name}' by {logEntry?.UserResponsible.Username ?? "Unknown"}");
			guild.GetChannel(Config.FetchModerationChannel(guild.Id)).SendMessageAsync(embed);
			return Task.CompletedTask;
		}

		private static Task MessageEmbed(DiscordGuild guild, DiscordMessage message, string actionType, DiscordMessage? pastMessage = null) {
			Task.Delay(1000);
			if(message.Author.IsBot)
				return Task.CompletedTask;

			if(pastMessage != null && pastMessage.Content == message.Content)
				return Task.CompletedTask;

			string attachments = "";
			foreach(DiscordAttachment attachment in message.Attachments)
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
    }
}

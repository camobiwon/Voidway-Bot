using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

namespace Voidway_Bot {
	internal class Moderation {
		internal static void HandleModeration(DiscordClient discord) {
			discord.GuildMemberRemoved += (client, e) => KickHandler(e);
			discord.GuildMemberUpdated += (client, e) => TimeoutHandler(e);
			discord.GuildBanAdded += (client, e) => ModerationEmbed(e.Guild, e.Member, "Banned", DiscordColor.Red);
			discord.GuildBanRemoved += (client, e) => ModerationEmbed(e.Guild, e.Member, "Ban Removed", DiscordColor.Green);
			discord.MessageDeleted += (client, e) => MessageEmbed(e.Guild, e.Message, "Deleted");
			discord.MessageUpdated += (client, e) => MessageEmbed(e.Guild, e.Message, "Edited", e.MessageBefore);
		}

		private static Task TimeoutHandler(GuildMemberUpdateEventArgs e) {
			if(e.CommunicationDisabledUntilAfter.HasValue && e.CommunicationDisabledUntilAfter > DateTime.Now)
				ModerationEmbed(e.Guild, e.Member, $"Timed Out", DiscordColor.Yellow, "Until", $"<t:{e.CommunicationDisabledUntilAfter.Value.ToUnixTimeSeconds()}:f>");
			else if(e.CommunicationDisabledUntilBefore.HasValue && !e.CommunicationDisabledUntilAfter.HasValue)
				ModerationEmbed(e.Guild, e.Member, $"Timeout Removed", DiscordColor.Gray);

			return Task.CompletedTask;
		}

		private static Task KickHandler(GuildMemberRemoveEventArgs e) {
			Task.Delay(1000);
			if(e.Guild.GetAuditLogsAsync(1).Result[0].ActionType != AuditLogActionType.Kick)
				return Task.CompletedTask;

			ModerationEmbed(e.Guild, e.Member, "Kicked", DiscordColor.Orange);
			return Task.CompletedTask;
		}

		private static Task ModerationEmbed(DiscordGuild guild, DiscordMember victim, string actionType, DiscordColor color, string customFieldTitle = "", string customField = "") {
			Task.Delay(1000);
			DiscordAuditLogEntry log = guild.GetAuditLogsAsync(1).Result[0];

			DiscordEmbedBuilder embed = new() {
				Title = $"User {actionType}",
				Color = color,
				Footer = new DiscordEmbedBuilder.EmbedFooter() { Text = $"User ID: {victim.Id}" }
			};
			embed.AddField("User", $"{victim.Username}", true);
			embed.AddField("Moderator", $"{log.UserResponsible.Username}", true);
			if(!string.IsNullOrEmpty(log.Reason))
				embed.AddField("Reason", log.Reason, true);
			else if(!string.IsNullOrEmpty(customFieldTitle))
				embed.AddField(customFieldTitle, customField, true);

			Console.WriteLine($"{actionType} {victim.Username} ({victim.Id}) in {guild.Name} by {log.UserResponsible.Username}");
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
	}
}

using DSharpPlus;
using DSharpPlus.Entities;
using Newtonsoft.Json.Linq;

namespace Voidway_Bot {
	internal class ModUploads {
		//static int lastTotalMods = 0;

		internal static Task HandleModUploadsAsync(DiscordClient discord) {
			return Task.CompletedTask;
			/*
			while(true) {
				using var client = new HttpClient();
				var content = await client.GetStringAsync("https://api.mod.io/v1/games/3809/mods?api_key=");
				int modCount = (int)(JObject.Parse(content)["result_total"] ?? 0);

				if(lastTotalMods == 0)
					lastTotalMods = modCount;

				if(lastTotalMods != modCount) {
					DiscordEmbedBuilder embed = new() {
						Title = $"User {actionType}",
						Color = DiscordColor.Blue,
						Footer = new DiscordEmbedBuilder.EmbedFooter() { Text = $"User ID: {victim.Id}" }
					};
					embed.AddField("User", $"{victim.Username}", true);
					embed.AddField("Moderator", $"{log.UserResponsible.Username}", true);
					if(!string.IsNullOrEmpty(log.Reason))
						embed.AddField("Reason", log.Reason, true);

					//Console.WriteLine($"{actionType} {victim.Username} ({victim.Id}) in {guild.Name} by {log.UserResponsible.Username}");
					discord.SendMessageAsync(Config.FetchModsChannel()[0], embed);
				}

				lastTotalMods = modCount;

				Console.WriteLine(lastTotalMods);

				Task.Delay(5000);
			}
			*/
		}
	}
}

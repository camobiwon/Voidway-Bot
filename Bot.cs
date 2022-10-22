using DSharpPlus;

namespace Voidway_Bot {
	class Bot {
		static void Main() {
			MainAsync().GetAwaiter().GetResult();
		}

		static async Task MainAsync() {
			string tokenPath = Path.Combine(AppContext.BaseDirectory, "token.txt");
			Console.WriteLine("Token loading from: " + tokenPath);
			string token = File.ReadAllLines(tokenPath)[0];
			if(string.IsNullOrEmpty(token)) {
				Console.WriteLine("token.txt is missing! Add one next to the executable, paste your token in, and rerun!");
				Console.ReadKey();
				Environment.Exit(0);
			}

			var discord = new DiscordClient(new DiscordConfiguration() {
				Token = token,
				TokenType = TokenType.Bot,
				Intents = DiscordIntents.All
			});

			Moderation.HandleModeration(discord);
			_ = ModUploads.HandleModUploadsAsync(discord);

			await discord.ConnectAsync();
			await Task.Delay(-1);
		}
	}
}
using DSharpPlus;
using Microsoft.Extensions.Logging;

namespace Voidway_Bot {
	class Bot {
		static void Main() {
			MainAsync().GetAwaiter().GetResult();
		}

		static async Task MainAsync() {
            SetupProcessLogs();
            string token = Config.GetToken();
			if(string.IsNullOrEmpty(token)) {
				Logger.Put("Config file is missing a token! Paste your token in and rerun!", Logger.Reason.Fatal);
				Console.ReadKey();
				Environment.Exit(0);
			}

			var discord = new DiscordClient(new DiscordConfiguration()
			{
				Token = token,
				TokenType = TokenType.Bot,
				Intents = DiscordIntents.All,
				LoggerFactory = new DiscordLogger.Factory()
			});

			discord.Ready += Discord_Ready;

			Moderation.HandleModeration(discord);
			_ = ModUploads.HandleModUploadsAsync(discord);

			await discord.ConnectAsync();
			await Task.Delay(-1);
		}

		static void SetupProcessLogs()
		{
            AppDomain.CurrentDomain.UnhandledException += LogUnhandledException;
            Console.CancelKeyPress += Console_CancelKeyPress;
        }

        private static void Console_CancelKeyPress(object? sender, ConsoleCancelEventArgs e)
        {
            Logger.Error("Operator pressed Ctrl+C, exiting now.");
            Console.WriteLine();
            Environment.Exit(0);
        }

        private static void LogUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
			Console.WriteLine();
            var exc = e.ExceptionObject as Exception;
            Logger.Error(exc?.ToString() ?? "Unknown exception");
            Console.ReadKey();
            Environment.Exit(0);
        }


        private static Task Discord_Ready(DiscordClient sender, DSharpPlus.EventArgs.ReadyEventArgs e)
        {
			Logger.Put($"Discord client ready on user {sender.CurrentUser.Username}#{sender.CurrentUser.Discriminator} ({sender.CurrentUser.Id})");
			return Task.CompletedTask;
        }
    }
}
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.Interactivity;
using DSharpPlus.Interactivity.Extensions;
using DSharpPlus.SlashCommands;
using Microsoft.Extensions.Logging;

namespace Voidway_Bot {
    class Bot {
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        internal static DiscordUser CurrUser { get; private set; }
        internal static DiscordClient CurrClient { get; private set; }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

        static void Main() {
            MainAsync().GetAwaiter().GetResult();
        }

        static async Task MainAsync() {
            SetupProcessLogs();
            string token = Config.GetDiscordToken();
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
                LoggerFactory = new DiscordLogger.Factory() // comment this line if slash commands are giving you trouble
            });
            CurrClient = discord;

            discord.Ready += Discord_Ready;
            discord.MessageCreated += DirectMessageHandler;

            var slashExtension = discord.UseSlashCommands();
            slashExtension.RegisterCommands<SlashCommands>();
            discord.ComponentInteractionCreated += SlashCommands.ComponentInteractionCreated;
            discord.UseInteractivity(new InteractivityConfiguration()
            {
                Timeout = TimeSpan.FromSeconds(30)
            });
            // setup interactivity now. will likely be useful later

            Moderation.HandleModeration(discord);
            ModUploads.HandleModUploads(discord);

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


        private static Task Discord_Ready(DiscordClient sender, ReadyEventArgs e)
        {
            CurrUser = sender.CurrentUser;
            Logger.Put($"Discord client ready on user {sender.CurrentUser.Username}#{sender.CurrentUser.Discriminator} ({sender.CurrentUser.Id})");
            return Task.CompletedTask;
        }

        private static Task DirectMessageHandler(DiscordClient sender, MessageCreateEventArgs e)
        {
            if (e.Channel.IsPrivate)
            {
                Logger.Put($"DM from {e.Author.Username}#{e.Author.Discriminator} ({e.Author.Id}): {e.Message.Content}");
                if (e.Message.Attachments.Count != 0)
                    Logger.Put($"DM from {e.Author.Username} has attachments: {string.Join("\n\t", e.Message.Attachments.Select(a => a.Url))}", Logger.Reason.Normal, false);
                // embeds are just shit that can be seen from the message content (like youtube)
                // if (e.Message.Embeds.Count != 0) 
                //     Logger.Put($"DM from {e.Author.Username} has embeds: {string.Join("\n\t", e.Message.Embeds.Select(e => e.Url))}", Logger.Reason.Normal, false);
            }

            return Task.CompletedTask;
        }
    }
}
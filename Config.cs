using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using Tomlet;
using Tomlet.Attributes;
using Tomlet.Models;

namespace Voidway_Bot {
    internal static class Config {
        internal class ConfigValues
        {
            [TomlProperty("token")] // property-ize because otherwise it throws a shitfit
            public string DiscordToken { get; set; } = "";
            public string modioToken = "";
            [TomlPrecedingComment("Can be left blank if you only use an API key w/o OAuth2")]
            public string modioOAuth = "";
            public string logPath = "./logs/";
            public int maxLogFiles = 5;
            public int auditLogRetryCount = 5;
            public bool logDiscordDebug = false;
            [TomlPrecedingComment("Allows thread creators to PIN messages in threads they created.")]
            public bool threadCreatorPinMessages = true;
            [TomlPrecedingComment("Allows thread creators to DELETE messages in threads they created.")]
            public bool threadCreatorDeleteMessages = false;
            [TomlPrecedingComment("Key=ServerID -> Value=ChannelID; Will be used for logging actions taken by moderators.")]
            public Dictionary<string, ulong> moderationChannels = new() { { "0", 1 }, { "2", 3 } }; // init w/ default values so the user knows how its formatted
            [TomlPrecedingComment("Key=ServerID -> Value=ChannelID; Will be used for logging message actions by users.")]
            public Dictionary<string, ulong> messsageChannels = new() { { "4", 5 } }; // <string,ulong> because otherwise tomlet shits itself and refuses to deserialize
            [TomlPrecedingComment("Where the bot will log suspicious joins. (<1d old & acc creation time within 1h of join time)")]
            public Dictionary<string, ulong> newAccountChannels = new() { { "6", 7 } }; // <string,ulong> because otherwise tomlet shits itself and refuses to deserialize
            [TomlPrecedingComment("Where ALL mod uploads get posted to, useful for seeing an entire list for moderation")]
            public Dictionary<string, ulong> allModUploads = new() { { "8", 9 } }; // <string,ulong> because otherwise tomlet shits itself and refuses to deserialize
            [TomlPrecedingComment("ServerID -> Upload Type -> ChannelID; Will be used for announcing recent mod.io uploads (Upload types: 'Avatar', 'Level', 'Spawnable', 'Utility').")]
            public Dictionary<string, Dictionary<string, ulong>> modUploadChannels = new() 
            { 
                { 
                    "10", new() { { nameof(ModUploads.UploadType.Avatar), 11 } } 
                } 
            };
			[TomlPrecedingComment("Will hide image & desc of mod announcements when posted in these servers AS LONG AS THEY MATCH THE SPECIFIED CRITERIA")]
			public ulong[] censorModAnnouncementsIn = new ulong[] { 12 };
			[TomlPrecedingComment("Determines if 'All' criteria, or just 'One' criterion must be met before a mod's announcement is censored. All criteria are in LOWERCASE, and can be set to '*' to match every mod (for censorCriteriaBehavior = All)")]
			public ModUploads.CensorCriteriaBehavior censorCriteriaBehavior = ModUploads.CensorCriteriaBehavior.One;
			public string[] censorModsWithSummaryContaining = new string[] { "ten point five" };
			public string[] censorModsWithTitlesContaining = new string[] { "eleven and no fraction", "This will never be hit because T is uppercase." };
			public string[] censorModsWithTag = new string[] { "ELEVEN POINT FIVE THAT WONT BE HIT CUZ CAPS", "adult 18+", "other tag" };
			public bool ignoreTagspamMods = true;
            [TomlPrecedingComment("Renames users to 'hoist' if their nick/name starts with one of these characterss (and is in a specified server). Backslash escape char FYI.")]
            public string hoistCharacters = @"()-+=_][\|;',.<>/?!@#$%^&*"; // literal string literal ftw
            public ulong[] hoistServers = new ulong[] { 13 };
            [TomlPrecedingComment("Deletes activity join invites in these servers.")]
            public ulong[] msgFilterServers = new ulong[] { 14 };
            [TomlPrecedingComment("Allows the invites in these channels, even if they're in a filtering server.")]
            public ulong[] msgFilterExceptions = new ulong[] { 15 };
            [TomlPrecedingComment("The invites to send when filtering someone's message.")]
            public string[] sendWhenFilterMessage = new string[] { "discord.gg/real" };
            [TomlPrecedingComment("The time between sending a message filter response to sending another message filter response, if someone else posts a new invite, and the time to leave the message up.")]
            public int msgFilterMessageTimeout = 60;
            public int msgFilterMessageStayTime = 10;
            [TomlPrecedingComment("Not necessarily able to bypass permissions (like Slash Commands) checks, just able to access debug commands/")]
            public ulong[] owners = new ulong[] { 16 };
            public string[] ignoreDSharpPlusLogsWith = new string[] { "Unknown event:" }; // "GUILD_JOIN_REQUEST_UPDATE" SHUT THE FUCK UP
        }

		const string FILE_NAME = "config.toml";
		static readonly FileSystemWatcher watcher;
        static readonly string activePath;
		static ConfigValues values;

        // static ctor
        static Config()
		{
            activePath = Path.Combine(AppContext.BaseDirectory, FILE_NAME);
            Console.WriteLine("Attempting to read config from " + activePath);

            if (!File.Exists(activePath))
            {
                WriteConfig(new ConfigValues()).Wait();
                Console.WriteLine("Config file wasn't found! An empty one was created, fill it out.");
                Console.ReadKey();
                Environment.Exit(0);
            }

			LoadConfig();

            WriteConfig(values).Wait();
			// write new cfg to add new fields
			Logger.Put("Updated config.");
			Logger.Put("(Updating config is harmless, just in case things changed between versions, this adds the new fields)", Logger.Reason.Trace);
			Logger.Put("Starting config watcher.");
			watcher = new FileSystemWatcher(AppContext.BaseDirectory)
			{
				Filter = "*.toml",
				IncludeSubdirectories = false,
				EnableRaisingEvents = true,
			};
            watcher.Changed += WatcherChanged;
			Logger.Put("Watcher started successfully.");
        }

        private static async void WatcherChanged(object sender, FileSystemEventArgs e)
        {
			// wait 25ms to avoid race conditions about reading while another process has access
			await Task.Delay(25);
            try
            {
			    LoadConfig();
            }
            catch (Exception ex)
            {
                Logger.Warn("Exception while live-reloading config file.", ex);
            }
        }

        [MemberNotNull(nameof(values))]
		private static void LoadConfig()
		{
            string fileContents = File.ReadAllText(activePath);
            values = TomletMain.To<ConfigValues>(fileContents);
            Logger.Put("Retrieved config values.");
        }

        internal static int GetAuditLogRetryCount() => values.auditLogRetryCount;
		internal static int GetMaxLogFiles() => values.maxLogFiles;
		internal static bool GetLogDiscordDebug() => values.logDiscordDebug;
		internal static bool GetThreadCreatorPinMessages() => values.threadCreatorPinMessages;
		internal static bool GetThreadCreatorDeleteMessages() => values.threadCreatorDeleteMessages;
        internal static string GetLogPath() => Path.GetFullPath(values.logPath);
		internal static ModUploads.CensorCriteriaBehavior GetCriteriaBehavior() => values.censorCriteriaBehavior;
		internal static bool GetIgnoreTagspam() => values.ignoreTagspamMods;
        internal static int GetFilterResponseTimeout() => values.msgFilterMessageTimeout;
        internal static string[] GetFilterInvites() => values.sendWhenFilterMessage;
        internal static int GetFilterResponseStayTime() => values.msgFilterMessageStayTime;
        internal static string GetDiscordToken() => values.DiscordToken;
		internal static (string, string) GetModioTokens() => (values.modioToken, values.modioOAuth);

        internal static ulong FetchModerationChannel(ulong guild) {
            if (values.moderationChannels.TryGetValue(guild.ToString(), out ulong channel)) return channel;
            else
            {
                Logger.Warn("Config values don't have a moderation log channel for the given guild ID: " + guild);
                return default;
            }
        }
		
		internal static ulong FetchMessagesChannel(ulong guild) {
			if (values.messsageChannels.TryGetValue(guild.ToString(), out ulong channel)) return channel;
			else
			{
				Logger.Warn("Config values don't have a messages channel for the given guild ID: " + guild);
                return default;
            }
        }

		internal static ulong FetchAllModsChannel(ulong guild) {
			if(values.allModUploads.TryGetValue(guild.ToString(), out ulong channel)) return channel;
			else {
				Logger.Warn("Config values don't have a all mod uploads channel for the given guild ID: " + guild);
				return default;
			}
		}

		internal static ulong FetchUploadChannel(ulong guild, ModUploads.UploadType uploadType) {
            if (!values.modUploadChannels.TryGetValue(guild.ToString(), out var uploadTypeToChannel))
                return default;

            if (!uploadTypeToChannel.TryGetValue(uploadType.ToString(), out ulong channel)) 
                return default;

            return channel;
        }

        internal static ulong FetchNewAccountLogChannel(ulong guild)
        {
            if (values.newAccountChannels.TryGetValue(guild.ToString(), out ulong channel)) return channel;
            else
            {
                // don't log, because some servers wont want to log new users (like the SLZ server)
                // Logger.Warn("Config values don't have a messages channel for the given guild ID: " + guild);
                return default;
            }
        }

        internal static bool IsHoistServer(ulong guild)
        {
            return values.hoistServers.Contains(guild);
        }

        internal static bool IsFilterMessageServer(ulong guild)
        {
            return values.msgFilterServers.Contains(guild);
        }

        internal static bool IsMessageAllowedChannel(ulong guild)
        {
            return values.msgFilterExceptions.Contains(guild);
        }

        internal static bool IsHoistMember(char firstChar)
        {
            return values.hoistCharacters.Contains(firstChar);
        }

        internal static bool IsDSharpPlusMessageIgnored(string message)
        {
            foreach (string ignoreWith in values.ignoreDSharpPlusLogsWith)
            {
                if (message.Contains(ignoreWith)) return true; // this may be a bit wasteful, speed-wise, but oh well it prevents logspam.
            }

            return false;
        }

        internal static bool IsServerCensoringMods(ulong guild)
        {
            return values.censorModAnnouncementsIn.Contains(guild);
        }

        internal static bool IsModSummaryCensored(string? description)
        {
            string? desc = description?.ToLower();
            if (desc is null) return false;

            foreach (string censorModsWith in values.censorModsWithSummaryContaining)
            {
                if (desc.Contains(censorModsWith)) return true;
                else if (censorModsWith == "*") return true;
            }

            return false;
        }

        internal static bool IsModTitleCensored(string? title)
        {
            string? modTitle = title?.ToLower(); // so tempted to name this local "tit" for lols
            if (modTitle is null) return false;

            foreach (string censorModsWith in values.censorModsWithTitlesContaining)
            {
                if (modTitle.Contains(censorModsWith)) return true;
                else if (censorModsWith == "*") return true;
            }

            return false;
        }

        internal static bool IsModTagsCensored(string[] modTags) // grammatically should be AreModTagsCensored but ive got a naming convention going on
        {
            string[] tags = modTags.Select(s => s.ToLower()).ToArray();

            foreach (string censorModsWith in values.censorModsWithTag)
            {
                if (tags.Contains(censorModsWith)) return true;
                else if (censorModsWith == "*") return true;
            }

            return false;
        }

        internal static bool IsUserOwner(ulong id) => values.owners.Contains(id);

        internal static Task ModifyConfig(Action<ConfigValues> changeVia)
        {
            StackTrace trace = new(1);
            MethodBase? mb = trace.GetFrame(0)?.GetMethod();
            Logger.Put("Config being modified from: " + (mb?.DeclaringType?.FullName ?? "<Unknown type>") + (mb?.Name ?? "<Unknown method>"), Logger.Reason.Trace);
            changeVia(values);
            return WriteConfig(values);
        }

        internal static async Task WriteConfig(ConfigValues cfg)
        {
            string fileContents = TomletMain.DocumentFrom(cfg).SerializedValue;
            await File.WriteAllTextAsync(activePath, fileContents);
            Logger.Put("Wrote config to disk.");
        }
    }
}

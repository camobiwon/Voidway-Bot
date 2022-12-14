using Tomlet;
using Tomlet.Attributes;
using Tomlet.Models;

namespace Voidway_Bot {
    internal static class Config {
        private class ConfigValues
        {
            [TomlProperty("token")] // property-ize because otherwise it throws a shitfit
            public string discordToken { get; set; } = "";
            public string modioToken = "";
            [TomlPrecedingComment("Can be left blank if you only use an API key w/o OAuth2")]
            public string modioOAuth = "";
            public string logPath = "./logs/";
            public int maxLogFiles = 5;
            public int auditLogRetryCount = 5;
            public bool logDiscordDebug = false;
            [TomlPrecedingComment("Key=ServerID -> Value=ChannelID; Will be used for logging actions taken by moderators.")]
            public Dictionary<string, ulong> moderationChannels = new() { { "0", 1 }, { "2", 3 } }; // init w/ default values so the user knows how its formatted
            [TomlPrecedingComment("Key=ServerID -> Value=ChannelID; Will be used for logging message actions by users.")]
            public Dictionary<string, ulong> messsageChannels = new() { { "4", 5 } }; // <string,ulong> because otherwise tomlet shits itself and refuses to deserialize
            [TomlPrecedingComment("Where the bot will log suspicious joins. (<1d old & acc creation time within 1h of join time)")]
            public Dictionary<string, ulong> newAccountChannels = new() { { "6", 7 } }; // <string,ulong> because otherwise tomlet shits itself and refuses to deserialize
            [TomlPrecedingComment("ServerID -> Upload Type -> ChannelID; Will be used for announcing recent mod.io uploads (Upload types: 'Avatar', 'Level', 'Spawnable', 'Utility').")]
            public Dictionary<string, Dictionary<string, ulong>> modUploadChannels = new() 
            { 
                { 
                    "8", new() { { nameof(ModUploads.UploadType.Avatar), 9 } } 
                } 
            };
            [TomlPrecedingComment("RoleID list")]
            public ulong[] rolesExemptFromLogging = Array.Empty<ulong>(); // ExemptRoleLog isnt called anywhere... does this need to exist?
            [TomlPrecedingComment("Will hide image & desc of mod announcements when posted in these servers AS LONG AS THEY MATCH THE SPECIFIED CRITERIA")]
            public ulong[] censorModAnnouncementsIn = new ulong[] { 10 };
            [TomlPrecedingComment("Determines if 'All' criteria, or just 'One' criterion must be met before a mod's announcement is censored. All criteria are in LOWERCASE, and can be set to '*' to match every mod (for censorCriteriaBehavior = All)")]
            public ModUploads.CensorCriteriaBehavior censorCriteriaBehavior = ModUploads.CensorCriteriaBehavior.One;
            public string[] censorModsWithSummaryContaining = new string[] { "ten point five" };
            public string[] censorModsWithTitlesContaining = new string[] { "eleven and no fraction", "This will never be hit because T is uppercase." };
            public string[] censorModsWithTag = new string[] { "ELEVEN POINT FIVE THAT WONT BE HIT CUZ CAPS", "adult 18+", "other tag" };
            [TomlPrecedingComment("Renames users to 'hoist' if their nick/name starts with one of these characterss (and is in a specified server). Backslash escape char FYI.")]
            public string hoistCharacters = @"()-+=_][\|;',.<>/?!@#$%^&*"; // literal string literal ftw
            public ulong[] hoistServers = new ulong[] { 12 };
            public string[] ignoreDSharpPlusLogsWith = new string[] { "Unknown event:" }; // "GUILD_JOIN_REQUEST_UPDATE" SHUT THE FUCK UP
        }

        const string FILE_NAME = "config.toml";
        static readonly ConfigValues values;

        // static ctor
        static Config()
        {
            string path = Path.Combine(AppContext.BaseDirectory, FILE_NAME);
            string fileContents;
            Console.WriteLine("Attempting to read config from " + path);

            if (!File.Exists(path))
            {
                TomlDocument doc = TomletMain.DocumentFrom(new ConfigValues());
                File.WriteAllText(path, doc.SerializedValue);
                Console.WriteLine("Config file wasn't found! An empty one was created, fill it out.");
                Console.ReadKey();
                Environment.Exit(0);
            }
            fileContents = File.ReadAllText(path);
            values = TomletMain.To<ConfigValues>(fileContents);

            Logger.Put("Retrieved config values.");

            // write new cfg to add new fields
            fileContents = TomletMain.DocumentFrom(values).SerializedValue;
            File.WriteAllText(path, fileContents);
            Logger.Put("Updated config.");
            Logger.Put("(Updating config is harmless, just in case things changed between versions, this adds the new fields)", Logger.Reason.Trace);
        }

        internal static int GetAuditLogRetryCount() => values.auditLogRetryCount;
        internal static int GetMaxLogFiles() => values.maxLogFiles;
        internal static bool GetLogDiscordDebug() => values.logDiscordDebug;
        internal static string GetLogPath() => Path.GetFullPath(values.logPath);
        internal static ModUploads.CensorCriteriaBehavior GetCriteriaBehavior() => values.censorCriteriaBehavior;

        internal static string GetDiscordToken()
        {
            string temp = values.discordToken;
            values.discordToken = "";
            return temp;
        }

        internal static (string, string) GetModioTokens()
        {
            string temp = values.modioToken;
            string temp2 = values.modioOAuth;
            values.modioToken = "";
            values.modioOAuth = "";
            return (temp, temp2);
        }

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

        internal static bool ExemptRoleLog(ulong roleID) {
            return values.rolesExemptFromLogging.Contains(roleID);
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
    }
}

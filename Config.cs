using DSharpPlus;
using DSharpPlus.Entities;
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
            public Dictionary<string, ulong> messageChannels = new() { { "4", 5 } }; // <string,ulong> because otherwise tomlet shits itself and refuses to deserialize
            [TomlPrecedingComment("Where the bot will log suspicious joins. (<1d old & acc creation time within 1h of join time)")]
            public Dictionary<string, ulong> newAccountChannels = new() { { "6", 7 } }; // <string,ulong> because otherwise tomlet shits itself and refuses to deserialize
            [TomlPrecedingComment("Where the bot will log flagged message from OpenAI.")]
            public Dictionary<string, ulong> openAiDiscordMonitorLogChannels = new() { { "8", 9 } }; // <string,ulong> because otherwise tomlet shits itself and refuses to deserialize
            [TomlPrecedingComment("Key=ServerID -> Value=ChannelID; Where ALL mod uploads get posted to, useful for seeing an entire list for moderation")]
            public Dictionary<string, ulong> allModUploads = new() { { "10", 11 } }; // <string,ulong> because otherwise tomlet shits itself and refuses to deserialize
            [TomlPrecedingComment("Key=ServerID -> Value=ChannelID; Where announcements of malformed uploads are sent (A staff-only channel)")]
            public Dictionary<string, ulong> malformedUploadChannels = new() { { "12", 13 } };
            [TomlPrecedingComment("ServerID -> Upload Type -> ChannelID; Will be used for announcing recent mod.io uploads (Upload types: 'Avatar', 'Level', 'Spawnable', 'Utility').")]
            public Dictionary<string, Dictionary<string, ulong>> modUploadChannels = new() 
            { 
                { 
                    "14", new() { { nameof(ModUploads.UploadType.Avatar), 15 } } 
                } 
            };
            [TomlPrecedingComment("Will hide image & desc of mod announcements when posted in these servers AS LONG AS THEY MATCH THE SPECIFIED CRITERIA")]
            public ulong[] censorModAnnouncementsIn = [16];
            [TomlPrecedingComment("Determines if 'All' criteria, or just 'One' criterion must be met before a mod's announcement is censored. All criteria are in LOWERCASE, and can be set to '*' to match every mod (for censorCriteriaBehavior = All)")]
            public ModUploads.CensorCriteriaBehavior censorCriteriaBehavior = ModUploads.CensorCriteriaBehavior.One;
            public string[] censorModsWithSummaryContaining = ["ten point five"];
            public string[] censorModsWithTitlesContaining = ["eleven and no fraction", "This will never be hit because T is uppercase."];
            public string[] censorModsWithTag = ["ELEVEN POINT FIVE THAT WONT BE HIT CUZ CAPS", "adult 18+", "other tag"];
            public bool ignoreTagspamMods = true;
            [TomlPrecedingComment("Renames users to 'hoist' if their nick/name starts with one of these characterss (and is in a specified server). Backslash escape char FYI.")]
            public string hoistCharacters = @"()-+=_][\|;',.<>/?!@#$%^&*"; // literal string literal ftw
            public ulong[] hoistServers = [14];
            [TomlPrecedingComment("Deletes activity join invites in these servers.")]
            public ulong[] msgFilterServers = [15];
            [TomlPrecedingComment("Allows invites in these channels, even if they're in a filtering server.")]
            public ulong[] msgFilterExceptions = [16];
            [TomlPrecedingComment("The invites to send when filtering someone's message.")]
            public string[] sendWhenFilterMessage = ["discord.gg/real"];
            [TomlPrecedingComment("The time between sending a message filter response to sending another message filter response, if someone else posts a new invite, and the time to leave the message up.")]
            public int msgFilterMessageTimeout = 60;
            public int msgFilterMessageStayTime = 10;
            [TomlPrecedingComment("Not necessarily able to bypass permissions (like Slash Commands) checks, just able to access debug commands/")]
            public ulong[] owners = [17];
            public string[] ignoreDSharpPlusLogsWith = ["Unknown event:"]; // "GUILD_JOIN_REQUEST_UPDATE" SHUT THE FUCK UP
            public Dictionary<string, ulong> modioCommentModerationNotifChannels = new() { { "18", 19 } };
            [TomlPrecedingComment("Doesn't run these channels' messages through OpenAI's Moderation endpoint.")]
            public ulong[] openAiModerationExceptions = [16];
            [TomlPrecedingComment("If populated, will use the *free* OpenAI Moderation endpoint to flag discord messages and/or mod.io comments given their content.")]
            public string openAiApiKey = "";
            public bool openAiModerateDiscord = false;
            public bool openAiModerateModio = true;
            public Dictionary<string, ulong> serverModNotesChannels = new() { { "20", 21 } };
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

            WriteConfig(values).GetAwaiter().GetResult();
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
        internal static string GetOpenAiToken() => values.openAiApiKey;

        internal static ulong FetchModerationChannel(ulong guild) {
            if (values.moderationChannels.TryGetValue(guild.ToString(), out ulong channel)) return channel;
            else
            {
                Logger.Warn("Config values don't have a moderation log channel for the given guild ID: " + guild);
                return default;
            }
        }
        
        internal static ulong FetchMessagesChannel(ulong guild) {
            if (values.messageChannels.TryGetValue(guild.ToString(), out ulong channel)) return channel;
            else
            {
                Logger.Warn("Config values don't have a messages channel for the given guild ID: " + guild);
                return default;
            }
        }

        internal static ulong FetchAllModsChannel(ulong guild) {
            if(values.allModUploads.TryGetValue(guild.ToString(), out ulong channel)) return channel;
            else {
                Logger.Warn("Config values don't have an all mod uploads channel for the given guild ID: " + guild);
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

        internal static ulong FetchMalformedUploadChannel(ulong guild)
        {
            if (values.malformedUploadChannels.TryGetValue((guild.ToString()), out var channel)) 
                return channel;

            return default;
        }

        internal static ulong FetchCommentModerationChannel(ulong guild)
        {
            if (values.modioCommentModerationNotifChannels.TryGetValue((guild.ToString()), out var channel))
                return channel;

            return default;
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

        internal static ulong FetchOpenAiModerationChannel(ulong guild)
        {
            if (values.openAiDiscordMonitorLogChannels.TryGetValue(guild.ToString(), out ulong channel)) return channel;
            else
            {
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

        internal static bool IsJoinMessageAllowedIn(ulong channel)
        {
            return values.msgFilterExceptions.Contains(channel);
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

        internal static bool IsExemptFromOpenAiScanning(ulong discordChannel)
        {
            return values.openAiModerationExceptions.Contains(discordChannel);
        }

        internal static bool IsModeratingModioComments() => values.openAiModerateModio;

        internal static bool IsModeratingDiscordMessages() => values.openAiModerateDiscord;

        internal static bool IsUserOwner(ulong id) => values.owners.Contains(id);

        internal static ulong GetModNotesChannel(ulong guildId)
        {
            if (values.serverModNotesChannels.TryGetValue(guildId.ToString(), out ulong channelId))
            {
                return channelId;
            }

            return 0;
        }

        internal static async Task<DiscordChannel?> GetModNotesChannel(DiscordClient client, ulong guildId)
        {
            if (values.serverModNotesChannels.TryGetValue(guildId.ToString(), out ulong channelId))
            {
                DiscordChannel? channel = await FetchChannelFromJumpLink(client, $"https://discord.com/channels/{guildId}/{channelId}");
                return channel;
            }

            return null;
        }

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

        private static async Task<DiscordMessage?> FetchMessageFromJumpLink(DiscordClient client, string jumpLink)
        {
            string[] split = jumpLink.Split('/');
            if (split.Length < 3) return null;

            ulong guildId = ulong.Parse(split[4]);
            ulong channelId = ulong.Parse(split[5]);
            ulong messageId = ulong.Parse(split[6]);

            // wrap in try-catch because dsharpplus will throw if the guild or channel is not found (EPICK WIN!!!)
            try
            {
                DiscordGuild guild = await client.GetGuildAsync(guildId);
                if (guild is null) return null;

                DiscordChannel channel = guild.GetChannel(channelId);
                if (channel is null) return null;

                return await channel.GetMessageAsync(messageId);
            }
            catch (Exception ex)
            {
                Logger.Warn("Exception while fetching message from jump link.", ex);
                return null;
            }
        }

        private static async Task<DiscordChannel?> FetchChannelFromJumpLink(DiscordClient client, string jumpLink)
        {
            string[] split = jumpLink.Split('/');
            if (split.Length < 3) return null;

            ulong guildId = ulong.Parse(split[4]);
            ulong channelId = ulong.Parse(split[5]);

            // wrap in try-catch because dsharpplus will throw if the guild or channel is not found (EPICK WIN!!!)
            try
            {
                DiscordGuild guild = await client.GetGuildAsync(guildId);
                if (guild is null) return null;

                if (guild.Channels.TryGetValue(channelId, out DiscordChannel? channel))
                {
                    return channel;
                }

                static IEnumerable<DiscordChannel> GetThreads(DiscordChannel ch)
                {
                    return ch.Type == ChannelType.Text || ch.Type == ChannelType.News || ch.Type == ChannelType.GuildForum
                        ? ch.Threads // avoids an exception
                        : Enumerable.Empty<DiscordChannel>();
                }
                IEnumerable<DiscordChannel> channelsAndThreads = guild.Channels.Values.Concat(guild.Channels.Values.SelectMany(GetThreads));
                return channelsAndThreads.FirstOrDefault(c => c.Id == channelId);
            }
            catch (Exception ex)
            {
                Logger.Warn("Exception while fetching channel from jump link.", ex);
                return null;
            }
        }
    }
}

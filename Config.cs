using Tomlet;
using Tomlet.Attributes;
using Tomlet.Models;

namespace Voidway_Bot {
	internal static class Config {
		private class ConfigValues
		{
			public string token = "";
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
            public Dictionary<string, Dictionary<ModUploads.UploadType, ulong>> modUploadChannels = new() 
			{ 
				{ 
					"8", new() { { ModUploads.UploadType.Avatar, 9 } } 
				} 
			};
            [TomlPrecedingComment("RoleID list")]
			public ulong[] rolesExemptFromLogging = Array.Empty<ulong>(); // ExemptRoleLog isnt called anywhere... does this need to exist?
			[TomlPrecedingComment("Renames users to 'hoist' if their nick/name starts with one of these characterss (and is in a specified server). Backslash escape char FYI.")]
			public string hoistCharacters = @"()-+=_][\|;',.<>/?!@#$%^&*"; // literal string literal ftw
			public ulong[] hoistServers = new ulong[] { 10 };
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


        internal static string GetToken()
		{
			string temp = values.token;
			values.token = "";
			return temp;
		}

		//Yes I know this is terrible, eventually will add a proper config
		internal static ulong FetchModerationChannel(ulong guild) {
            if (values.moderationChannels.TryGetValue(guild.ToString(), out ulong channel)) return channel;
            else
            {
                Logger.Warn("Config values don't have a moderation log channel for the given guild ID: " + guild);
                return default;
            }
            //return guild switch {
            //	601515180232409119 => 601515180232409121, //Testing Server, #general
            //	563139253542846474 => 676246171731099678, //BONELAB, #staff-logs
            //	918643357998260246 => 918646789551312998, //Lava Gang, #moderators
            //	_ => 0
            //};
        }
		
		internal static ulong FetchMessagesChannel(ulong guild) {
			if (values.messsageChannels.TryGetValue(guild.ToString(), out ulong channel)) return channel;
			else
			{
				Logger.Warn("Config values don't have a messages channel for the given guild ID: " + guild);
                return default;
            }

			//return guild switch {
			//	601515180232409119 => 601515180232409121, //Testing Server, #general
			//	563139253542846474 => 1026091787493855293, //BONELAB, #message-logs
			//	918643357998260246 => 1026097314592464936, //Lava Gang, #message-logs
			//	_ => 0
			//};
		}

		internal static bool ExemptRoleLog(ulong roleID) {
			return values.rolesExemptFromLogging.Contains(roleID);
			//ulong[] exemptRoles = new ulong[] { 693733552692396063, 604409515630133258, 604409483929845760, 604409433509855262 };
			//foreach(ulong role in exemptRoles)
			//	if(roleID == role)
			//		return true;

			//return false;
		}

		internal static ulong FetchUploadChannel(ulong guild, ModUploads.UploadType uploadType) {
			//return new ulong[] { 601515180232409121 /* Testing Server, #general */ };
			if (!values.modUploadChannels.TryGetValue(guild.ToString(), out var uploadTypeToChannel)) return default;

			if (!uploadTypeToChannel.TryGetValue(uploadType, out ulong channel)) return default;

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
	}
}

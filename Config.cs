namespace Voidway_Bot {
	internal static class Config {
		//Yes I know this is terrible, eventually will add a proper config
		internal static ulong FetchModerationChannel(ulong guild) {
			return guild switch {
				601515180232409119 => 601515180232409121, //Testing Server, #general
				563139253542846474 => 676246171731099678, //BONELAB, #staff-logs
				918643357998260246 => 918646789551312998, //Lava Gang, #moderators
				_ => 0
			};
		}
		
		internal static ulong FetchMessagesChannel(ulong guild) {
			return guild switch {
				601515180232409119 => 601515180232409121, //Testing Server, #general
				563139253542846474 => 1026091787493855293, //BONELAB, #message-logs
				918643357998260246 => 1026097314592464936, //Lava Gang, #message-logs
				_ => 0
			};
		}

		internal static bool ExemptRoleLog(ulong roleID) {
			ulong[] exemptRoles = new ulong[] { 693733552692396063, 604409515630133258, 604409483929845760, 604409433509855262 };
			foreach(ulong role in exemptRoles)
				if(roleID == role)
					return true;

			return false;
		}

		internal static ulong[] FetchModsChannel() {
			return new ulong[] { 601515180232409121 /* Testing Server, #general */ };
		}
	}
}

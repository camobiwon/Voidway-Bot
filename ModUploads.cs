using DSharpPlus;
using Modio;
using DSharpPlus.Entities;
using Modio.Models;
using DSharpPlus.EventArgs;
using DSharpPlus.Exceptions;
using System.Threading.Channels;

namespace Voidway_Bot {
	internal static class ModUploads {
		[Flags]
		public enum UploadType // use bitshift operator to act as a bitfield
		{
			Unknown = 0,
			Avatar = 1 << 0,
			Level = 1 << 1,
			Spawnable = 1 << 2,
			Utility = 1 << 3,
		}
        // possible todo: alias "gun", "NPC" to spawnable

        public enum CensorCriteriaBehavior
		{
			All,
			One
		}

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
		static Dictionary<UploadType, List<DiscordChannel>> uploadChannels = new();
		static Dictionary<uint, List<DiscordMessage>> announcementMessages = new(); // modio mod id to message
		static List<string> uploadTypeNames = Enum.GetNames<UploadType>().ToList(); // List has IndexOf, Array does not
		static UploadType[] uploadTypeValues = Enum.GetValues<UploadType>();
		static UploadType[] uploadTypeValuesNoUnk = Enum.GetValues<UploadType>().Skip(1).ToArray(); // skip needs to be changed if Unknown is moved
		static Client client;
		static GameClient bonelab;
		static ModsClient bonelabMods;
		static uint lastModioEvent;
		// static Dictionary<uint, bool> censorModCache = new(); is it worth extra alloc's and shit to cache the result of WillCensor? my guess is nope.
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

		private static async void ModUploadWatcher()
		{
			while (true)
			{
				await Task.Delay(60 * 1000);

				IReadOnlyList<ModEvent> events;
				try {
					events = await bonelabMods.GetEvents(lastModioEvent).ToList();
					if (events == null) throw new NullReferenceException("Event list was null for some arbitrary reason.");
				} catch (Exception ex) {
					Logger.Warn($"Failed to fetch new mods! {ex}");
					continue;
				}

				foreach (var modEvent in events)
				{
					if (modEvent.Id > lastModioEvent) lastModioEvent = modEvent.Id;

					switch (modEvent.EventType)
					{
						case ModEventType.MOD_AVAILABLE:
							NotifyNewMod(modEvent.ModId, modEvent.UserId);
							break;
						case ModEventType.MOD_EDITED:
							UpdateAnnouncements(modEvent.ModId);
							break;
						default:
							break;
					}
				}
			}
		}

        internal static async void HandleModUploads(DiscordClient discord) {
			Credentials cred;

			(string token, string oa2) = Config.GetModioTokens();
			if (string.IsNullOrEmpty(token))
			{
				Logger.Warn("Missing api key for mod.io, continuing without mod announcements");
				return;
			}
			if (string.IsNullOrEmpty(oa2))
			{
				cred = new(token);
			}
			else cred = new(token, oa2);


			client = new(cred);
			User currUser = await client.User.GetCurrentUser();
			var games = await client.Games.Search().ToList();
			Game bonelabGame = games.First(g => g.NameId == "bonelab");
			bonelab = client.Games[bonelabGame.Id];
			bonelabMods = bonelab.Mods;
			ModEvent? firstEvent = await bonelab.Mods.GetEvents().First();
			lastModioEvent = firstEvent!.Id; // im quite certain theres at least one event

			discord.GuildDownloadCompleted += (client, e) => GetAnnouncementChannels(e.Guilds);

			ModUploadWatcher();

			Logger.Put($"Started watching for mod.io uploads in {bonelabGame.Name} (ID:{bonelabGame.Id},NameID:{bonelabGame.NameId}) on user {currUser.Username} (ID:{currUser.Id},NameID:{currUser.NameId})");

			return;
		}

        private static async void UpdateAnnouncements(uint modId)
        {
			bool hasMessages = announcementMessages.TryGetValue(modId, out List<DiscordMessage>? messages) && messages.Any();
			if (!hasMessages) return;
			bool embedsHaveImage = string.IsNullOrEmpty(messages![0].Embeds[0].Image.Url.ToString());
			if (embedsHaveImage) return;

			Mod mod;
			try
			{
				mod = await bonelabMods[modId].Get();
			}
			catch(Exception ex)
			{
				Logger.Warn($"Failed to fetch edited data about mod ID:{modId}, Details: {ex}");
				return;
			}

			if (mod.MaturityOption.HasFlag(MaturityOption.Explicit) || mod.Tags.Any(t => t.Name == "Adult 18+"))
			{
				Logger.Put($"Bailing on updating announcements of mod ID:{modId} because it's explicit/has the Adult tag.");
                return;
            }

			DiscordEmbedBuilder baseEmbed = CreateEmbed(mod);

			foreach (DiscordMessage msg in messages)
			{
                DiscordEmbedBuilder deb = new(baseEmbed);

                if (ShouldHideDesc(mod, msg.Channel.GuildId ?? 1UL))
                {
                    Logger.Put($"Hiding mod summary for {mod.NameId}");
                    deb.Description = "";
                }
				//Hide mature images
                if (ShouldHideImage(mod, msg.Channel.GuildId ?? 1UL))
                {
                    Logger.Put($"Hiding mod image for {mod.NameId}");
                    deb.ImageUrl = "";
                }

				try
				{
					await msg.ModifyAsync(deb.Build());
					Logger.Put($"Successfully edited message in guild {msg.Channel.Guild.Name} #{msg.Channel.Name} (msg ID:{msg.Id}) for mod {mod.Name} (mod ID:{modId}) to add image");
				}
				catch (Exception ex)
				{
					Logger.Warn($"Failed to edit mod announcement in guild {msg.Channel.Guild.Name} #{msg.Channel.Name} (msg ID:{msg.Id}) for mod {mod.Name} (mod ID:{modId}), Details: {ex}");
				}
            }

        }

        internal static async void NotifyNewMod(uint modId, uint userId)
		{
			// give the uploader 60 extra seconds to upload a thumbnail/change metadata/add tags
			await Task.Delay(15 * 1000);
			if (uploadChannels is null || uploadChannels.Count is 0) FallbackGetChannels();

			ModClient newMod = bonelabMods[modId];
			Mod modData;
			IReadOnlyList<Tag> tags;
			UploadType uploadType = UploadType.Unknown;
			try
			{
				modData = await newMod.Get();
				tags = await newMod.Tags.Get();
			}
			catch (Exception ex)
			{
				Logger.Warn($"Failed to fetch data about mod ID:{modId}, Details: {ex}");
				return;
			}
			Logger.Put($"New mod available: ID= {modId}; NameID= {modData.NameId}; tags= {string.Join(',', tags.Select(t => t.Name))}");


			if (modData.MaturityOption == MaturityOption.Explicit || tags.Any(t => t.Name == "Adult 18+"))
			{
				Logger.Put($"Bailing on posting mod: {modData.NameId} ({modId}) as mod is NSFW");
				return;
			}

			uploadType = IdentifyUpload(tags);
			Logger.Put($" - Mod {modData.NameId} has identified as: {uploadType}");

			if (uploadType == UploadType.Unknown)
			{
				Logger.Warn("Unrecognized mod type. It is recommended to look through tags and report to a developer (or fix this yourself).");
				return;
			}

			if (Config.GetIgnoreTagspam() && modData.Tags.Count > 15)
			{
				Logger.Put($"Mod has over 15 tags. Ignoring as it is likely a tagspam mod.");
                return;
            }

			await PostAnnouncements(modData, uploadType);
		}


		private static Task GetAnnouncementChannels(IEnumerable<KeyValuePair<ulong, DiscordGuild>> keyValues)
		{
			// NESTED FOREACH SO GOOD
			int counter = 0;
			foreach (UploadType uType in uploadTypeValues)
			{
				uploadChannels[uType] = new();

				foreach (var kvp in keyValues)
				{
					ulong channelId = Config.FetchUploadChannel(kvp.Key, uType);
					if(channelId == 0)
						channelId = Config.FetchAllModsChannel(kvp.Key);
					if(channelId == 0) //Dumb but works
						continue;


					DiscordChannel channel = kvp.Value.GetChannel(channelId);
					if (channel is null) continue;

					uploadChannels[uType].Add(channel);
					counter++;
				}
			}

			Logger.Put($"Fetched {counter} mod.io announcement channels");
			return Task.CompletedTask;
		}

		private static async Task PostAnnouncements(Mod mod, UploadType uploadType)
        {
            announcementMessages[mod.Id] ??= new();
            List<DiscordMessage> messages = announcementMessages[mod.Id];
            DiscordEmbedBuilder baseEmbed = CreateEmbed(mod);

            int count = 0;
            foreach (UploadType flag in uploadTypeValuesNoUnk)
            {
                if (!uploadType.HasFlag(flag)) continue;
                if (!uploadChannels.TryGetValue(flag, out List<DiscordChannel>? channels)) continue;

                foreach (DiscordChannel channel in channels)
                {
                    DiscordEmbedBuilder deb = new(baseEmbed);
                    if (ShouldHideDesc(mod, channel.GuildId ?? 1UL))
                    {
                        Logger.Put($"Hiding mod summary for {mod.NameId}");
                        deb.Description = "";
                    }
                    //Hide mature images
                    if (ShouldHideImage(mod, channel.GuildId ?? 1UL))
                    {
                        Logger.Put($"Hiding mod image for {mod.NameId}");
                        deb.ImageUrl = "";
                    }

                    try
                    {
                        messages.Add(await channel.SendMessageAsync(deb));
                        count++;
                    }
                    catch (DiscordException ex)
                    {
                        Logger.Warn($"Failed to post announcement for {mod.NameId} ({mod.Id}) in #{channel.Name} (guild {channel.Guild.Name}). Details below.");
                        Logger.Warn(ex.ToString());
                    }
                }
            }
            Logger.Put($"Announced mod upload in {count} channel(s).");
        }

        private static DiscordEmbedBuilder CreateEmbed(Mod mod)
        {
            string? image = mod.Media.Images.FirstOrDefault()?.Original?.ToString();
            DiscordEmbedBuilder.EmbedAuthor? author = mod.SubmittedBy is not null
                ? new DiscordEmbedBuilder.EmbedAuthor()
                {
                    Name = mod.SubmittedBy.Username?.ToString()!,
                    IconUrl = mod.SubmittedBy.Avatar?.Thumb50x50?.ToString()!,
                    Url = mod.SubmittedBy.ProfileUrl?.ToString()!
                }
                : null;
            //todo: make embed fancier
            DateTime start = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            DateTime date = start.AddMilliseconds(mod.DateLive / 10).ToLocalTime();
            DiscordEmbedBuilder baseEmbed = new DiscordEmbedBuilder()
            {
                Author = author!,
                Title = $"{mod.Name}",
                Description = mod.Summary!,
                Url = mod.ProfileUrl?.ToString()!,
                Color = DiscordColor.Azure,
                ImageUrl = image!,
                Footer = new DiscordEmbedBuilder.EmbedFooter { Text = $"ID: {mod.Id}" },
            };
            return baseEmbed;
        }

        private static bool ShouldHideImage(Mod mod, ulong server) 
			=> mod.MaturityOption != MaturityOption.None 
			|| WillCensor(mod, server);

		private static bool ShouldHideDesc(Mod mod, ulong server)
			=> mod.MaturityOption.HasFlag(MaturityOption.Explicit)
			|| WillCensor(mod, server);

        static UploadType IdentifyUpload(IEnumerable<Tag> tags)
		{
			UploadType ret = UploadType.Unknown;
			foreach (Tag tag in tags)
			{
				int uploadTypeIndex;
				if (string.IsNullOrEmpty(tag.Name)) continue; // ignore empty tags

				uploadTypeIndex = uploadTypeNames.IndexOf(tag.Name);

				if (uploadTypeIndex != -1)
					ret |= uploadTypeValues[uploadTypeIndex]; // support the use of Flags by using bitwise operations
			}
			return ret;
		}

		// "Censor" sounds 1984-y but this doesnt outright censor
		private static bool WillCensor(Mod mod, ulong server)
		{
			if (!Config.IsServerCensoringMods(server)) return false;

			bool desc = Config.IsModSummaryCensored(mod.DescriptionPlaintext);
			bool title = Config.IsModTitleCensored(mod.Name);

            return Config.GetCriteriaBehavior() switch
            {
                CensorCriteriaBehavior.All => desc && title,
                CensorCriteriaBehavior.One => desc || title,
                _ => throw new InvalidDataException($"Unrecognized {nameof(CensorCriteriaBehavior)}: {Config.GetCriteriaBehavior()}. Please input a valid value in the config."),
            };
        }

		static void FallbackGetChannels()
		{
			// have to do this because dsharpplus doesnt fire guilddownloadscompleted (or whatever the event is called) on .NET 7, for whatever reason
			Logger.Put("The list of mod upload announcement channels is empty! Attempting to rectify this now.", Logger.Reason.Debug);

			GetAnnouncementChannels(Bot.CurrClient.Guilds);
		}

    }
}

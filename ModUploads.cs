using DSharpPlus;
using Modio;
using DSharpPlus.Entities;
using Newtonsoft.Json.Linq;
using Modio.Models;
using System.IO;
using DSharpPlus.EventArgs;
using System.Threading.Channels;

namespace Voidway_Bot {
	internal static class ModUploads {
		public enum UploadType
		{
			Unknown,
			Avatar,
			Level,
			Spawnable,
			Utility
		}

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
		static Dictionary<UploadType, List<DiscordChannel>> uploadChannels = new();
		static Client client;
		static GameClient bonelab;
		static ModsClient bonelabMods;
		static uint lastModioEvent;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

		private static async void ModUploadWatcher()
		{
			while(true)
			{
                await Task.Delay(60 * 1000);

                IReadOnlyList<ModEvent> events;
				try {
					events = await bonelabMods.GetEvents(lastModioEvent).ToList();
					if(events == null) throw new NullReferenceException("Event list was null for some arbitrary reason.");
				} catch(Exception ex) {
					Logger.Warn($"Failed to fetch new mods! {ex}");
                    continue;
                }

                foreach (var modEvent in events)
                {
                    if (modEvent.Id > lastModioEvent) lastModioEvent = modEvent.Id;

                    if (modEvent.EventType == ModEventType.MOD_AVAILABLE)
					{
						await NotifyNewMod(modEvent.ModId, modEvent.UserId);
					}
                }
            }
		}

		// todo: FINISH THIS

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

			discord.GuildDownloadCompleted += (client, e) => GetAnnouncementChannels(e);

			ModUploadWatcher();

			Logger.Put($"Started watching for mod.io uploads in {bonelabGame.Name} (ID:{bonelabGame.Id},NameID:{bonelabGame.NameId}) on user {currUser.Username} (ID:{currUser.Id},NameID:{currUser.NameId})");

            return;
		}


		private static async Task NotifyNewMod(uint modId, uint userId)
		{
            ModClient newMod = bonelabMods[modId];
            Mod modData;
            IReadOnlyList<Tag> tags;
			UploadType uploadType = UploadType.Unknown;
			try {
				modData = await newMod.Get();
				tags = await newMod.Tags.Get();
			} catch(Exception ex) {
				Logger.Warn($"Failed to fetch data about mod ID:{modId}, Details: {ex}");
                return;
            }
            Logger.Put($"New mod available: ID= {modId}; NameID= {modData.NameId}; tags= {string.Join(',', tags.Select(t => t.Name))}");


			if (modData.MaturityOption == MaturityOption.Explicit)
			{
				Logger.Warn($"Bailing on posting mod: {modData.NameId}({modId}) as mod is NSFW");
				return;
			}

			List<string> uploadTags = Enum.GetNames(typeof(UploadType)).ToList();
			foreach(Tag tag in tags)
				if(!string.IsNullOrEmpty(tag.Name) && uploadTags.Contains(tag.Name))
					uploadType = (UploadType)(uploadTags.IndexOf(tag.Name) - 1);

			if (uploadType == UploadType.Unknown)
			{
				Logger.Warn("Unrecognized mod type. It is recommended to look through tags and report to a developer (or fix this yourself).");
				return;
			}

			await PostAnnouncements(modData, uploadType);
        }


		private static Task GetAnnouncementChannels(GuildDownloadCompletedEventArgs e)
		{
			// NESTED FOREACH SO GOOD
			int counter = 0;
			foreach (UploadType uType in Enum.GetValues<UploadType>())
			{
				uploadChannels[uType] = new();

                foreach (var kvp in e.Guilds)
				{
					ulong channelId = Config.FetchUploadChannel(kvp.Key, uType);
					if (channelId == 0) continue;


					DiscordChannel channel = kvp.Value.GetChannel(channelId);
					if (channel == null) continue;

					uploadChannels[uType].Add(channel);
					counter++;
				}
			}

			Logger.Put($"Fetched {counter} mod.io announcement channels");
			return Task.CompletedTask;
		}

		private static async Task PostAnnouncements(Mod mod, UploadType uploadType)
		{
			List<DiscordChannel> channels = uploadChannels[uploadType];
            string? image = mod.Media.Images.FirstOrDefault()?.Original?.ToString();
			DiscordEmbedBuilder.EmbedFooter? footer = mod.SubmittedBy is null
				? null
				: new DiscordEmbedBuilder.EmbedFooter() 
				{ 
					Text = $"Creator: {mod.SubmittedBy.Username}", 
					IconUrl = mod.SubmittedBy.Avatar?.Thumb50x50?.ToString() 
				};
			//todo: make embed fancier
			DiscordEmbedBuilder embed = new DiscordEmbedBuilder() {
				Title = $"{mod.Name}",
				Description = mod.Summary,
				Url = mod.ProfileUrl?.ToString(),
				Color = DiscordColor.Blue,
				ImageUrl = image,
				Footer = footer,
			};


			foreach (DiscordChannel channel in channels)
			{
				await channel.SendMessageAsync(embed);
			}
			Logger.Put($"Announced mod upload in {channels.Count} channel(s).");
        }
    }
}

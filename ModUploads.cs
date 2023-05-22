﻿using DSharpPlus;
using Modio;
using DSharpPlus.Entities;
using Modio.Models;
using DSharpPlus.EventArgs;
using DSharpPlus.Exceptions;
using System.Threading.Channels;
using ModFile = Modio.Models.File;
using System.IO.Compression;
using System.Net.Http.Json;

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

        [Flags]
        enum FileUploadHeuristic
        {
            UnrecognizedNoMod = 0,
            MarrowMod = 1 << 0,
            Txt = 1 << 1,
            Img = 1 << 2,
            Blend = 1 << 3,
            Fbx = 1 << 4,
            Misc3dFile = 1 << 5,
            UnityPkg = 1 << 6,
            UnityProj = 1 << 7, // i swear to god this has happened at least once. a full fucking unity project.
            VirusFlagged = 1 << 10,
        }

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        static Dictionary<UploadType, List<DiscordChannel>> uploadChannels = new();
        static Dictionary<uint, List<DiscordMessage>> announcementMessages = new(); // modio mod id to message
        static List<DiscordChannel> malformedFileChannels = new();
        static List<string> uploadTypeNames = Enum.GetNames<UploadType>().ToList(); // List has IndexOf, Array does not
        static UploadType[] uploadTypeValues = Enum.GetValues<UploadType>();
        static UploadType[] uploadTypeValuesNoUnk = Enum.GetValues<UploadType>().Skip(1).ToArray(); // skip needs to be changed if Unknown is moved
        static Client client;
        static GameClient bonelab;
        static ModsClient bonelabMods;
        [ThreadStatic] static HttpClient downloadClient; // threadstatic to prevent multiple threads using the same httpclient at the same time
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

            discord.GuildDownloadCompleted += (client, e) => GetChannelsFromGuilds(e.Guilds);

            ModUploadWatcher();

            Logger.Put($"Started watching for mod.io uploads in {bonelabGame.Name} (ID:{bonelabGame.Id},NameID:{bonelabGame.NameId}) on user {currUser.Username} (ID:{currUser.Id},NameID:{currUser.NameId})");

            return;
        }

        private static async void UpdateAnnouncements(uint modId)
        {
            bool hasMessages = announcementMessages.TryGetValue(modId, out List<DiscordMessage>? messages) && (messages?.Any() ?? false);
            if (!hasMessages) return;
            bool embedsHaveImage = !string.IsNullOrEmpty(messages!.FirstOrDefault()?.Embeds.FirstOrDefault()?.Image?.Url?.ToString());
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

            foreach (DiscordMessage msg in messages!)
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
            await Task.Delay(60 * 1000);
            if (uploadChannels is null || uploadChannels.Count is 0) FallbackGetChannels();

            ModClient newMod = bonelabMods[modId];
            IReadOnlyList<Modio.Models.File> files;
            Mod modData;
            IReadOnlyList<Tag> tags;
            UploadType uploadType = UploadType.Unknown;
            NotifyIfNoBundle(newMod);
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
            // force recheck after 5min, thanks modio api for working predictably :) (SARCASM)
            await Task.Delay(5 * 60 * 1000);
            UpdateAnnouncements(modId);
        }


        private static Task GetChannelsFromGuilds(IEnumerable<KeyValuePair<ulong, DiscordGuild>> keyValues)
        {
            // NESTED FOREACH SO GOOD
            int announcementChannelCounter = 0;
            int allAnnouncementChannelCounter = 0;
            int malformedChannelCounter = 0;

            foreach (var kvp in keyValues)
            {
                ulong malChannelId = Config.FetchMalformedUploadChannel(kvp.Key);
                DiscordChannel malChannel = kvp.Value.GetChannel(malChannelId);
                if (malChannel is not null)
                {
                    malformedFileChannels.Add(malChannel);
                    malformedChannelCounter++;
                }

                ulong allChannelId = Config.FetchAllModsChannel(kvp.Key);
                foreach (UploadType uType in uploadTypeValues)
                {
                    uploadChannels[uType] = new();

                    // getchannel does a tryget from a dict, returns null if not found. default will cause null to be ret'd
                    ulong upChannelId = Config.FetchUploadChannel(kvp.Key, uType);
                    DiscordChannel? upChannel = kvp.Value.GetChannel(upChannelId); 
                    DiscordChannel? allChannel = kvp.Value.GetChannel(allChannelId);


                    if (upChannel is not null)
                    {
                        uploadChannels[uType].Add(upChannel);
                        announcementChannelCounter++;
                    }

                    if (allChannel is not null)
                    {
                        uploadChannels[uType].Add(allChannel);
                        allAnnouncementChannelCounter++;
                    }
                }
            }
            Logger.Put($"Fetched {announcementChannelCounter + allAnnouncementChannelCounter + malformedChannelCounter} total mod.io upload announcement channels");
            Logger.Put($" - {announcementChannelCounter} per-type mod.io announcement channels");
            Logger.Put($" - {allAnnouncementChannelCounter} all-type mod.io announcement channels");
            Logger.Put($" - {malformedChannelCounter} malformed (moderation) announcement channels");
            return Task.CompletedTask;
        }

        private static async Task PostAnnouncements(Mod mod, UploadType uploadType)
        {
            if (!announcementMessages.TryGetValue(mod.Id, out List<DiscordMessage>? messages))
            {
                messages = new();
                announcementMessages[mod.Id] = messages;
            }
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

        static async void NotifyIfNoBundle(ModClient modClient)
        {
            //await Task.Delay(60 * 1000);
            // Wrap in try-catch because async void crashes program if excepted
            try
            {
                Mod mod = await modClient.Get();
                FileUploadHeuristic nonBundleType = await GetUploadFiletypes(modClient, mod);
                if (nonBundleType != FileUploadHeuristic.MarrowMod) await SendMalformedUploadMsgs(mod, nonBundleType);
            }
            catch(Exception ex) 
            {
                Logger.Warn($"Failed to notify a (possibly) bundle-less mod! Details: {ex}");
            }
        }

        static async Task<FileUploadHeuristic> GetUploadFiletypes(ModClient modClient, Mod mod)
        {
            /*
                UnrecognizedNoMod = 0,
                Txt = 1 << 0,
                Img = 1 << 1,
                Blend = 1 << 2,
                Fbx = 1 << 3,
                VirusFlagged = 1 << 4,
                UnityPkg = 1 << 5,
                UnityProj
            */
            ModFile? file =  await modClient.Files.Search().First();
            Download? download = file?.Download;
            downloadClient ??= new();
            FileUploadHeuristic ret = FileUploadHeuristic.UnrecognizedNoMod;
            if (file is null)
            {
                Logger.Warn($"Modio API had another skill issue: the first file on {mod.NameId} was null");
                return ret;
            }
            else if (download is null)
            {
                Logger.Warn($"Modio API had another skill issue: the download link for first file on {mod.NameId} was null");
                return ret;
            }

            // do everything in memory to avoid writing to slow pi storage
            using Stream stream = await downloadClient.GetStreamAsync(download.BinaryUrl);
            using ZipArchive zip = new(stream);
            string[] textExts = { ".txt", ".rtf", ".docx", };
            string[] imageExts = { ".jpg", ".jpeg", ".png", ".webp", ".gif", ".tiff", ".bmp" };
            string[] misc3dExts = { ".obj", ".stl", ".dae", ".glb" };
            string[] filePaths = zip.Entries.Select(ze => ze.FullName.ToLower()).ToArray();
            bool hasBundle = filePaths.Any(p => p.EndsWith(".bundle"));
            bool hasJson = filePaths.Any(p => p.EndsWith(".json")); // someone let that guy from Heavy Rain know
            bool hasHash = filePaths.Any(p => p.EndsWith(".hash")); // isCalifornian? (haha get it? weed joke)
            bool isLikelyValidMod = hasBundle && hasJson && hasHash;
            
            bool hasTxt = filePaths.Any(p => textExts.Contains(Path.GetExtension(p)));
            bool hasImg = filePaths.Any(p => imageExts.Contains(Path.GetExtension(p)));
            bool hasBlend = filePaths.Any(p => p.EndsWith(".blend"));
            bool hasFbx = filePaths.Any(p => p.EndsWith(".fbx"));
            bool hasOther3d = filePaths.Any(p => misc3dExts.Contains(Path.GetExtension(p)));
            bool virusFlagged = file.VirusStatus == 1 && file.VirusPositive == 1;
            bool hasUnityPkg = filePaths.Any(p => p.EndsWith(".unitypackage"));
            bool hasUnityProj = filePaths.Any(p => p.EndsWith(".meta"));

            if (isLikelyValidMod)
                ret |= FileUploadHeuristic.MarrowMod;
            if (hasTxt)
                ret |= FileUploadHeuristic.Txt;
            if (hasImg)
                ret |= FileUploadHeuristic.Img;
            if (hasBlend)
                ret |= FileUploadHeuristic.Blend;
            if (hasFbx)
                ret |= FileUploadHeuristic.Fbx;
            if (hasOther3d)
                ret |= FileUploadHeuristic.Misc3dFile;
            if (virusFlagged)
                ret |= FileUploadHeuristic.VirusFlagged;
            if (hasUnityPkg)
                ret |= FileUploadHeuristic.UnityPkg;
            if (hasUnityProj)
                ret |= FileUploadHeuristic.UnityProj;

            return ret;
        }

        static async Task SendMalformedUploadMsgs(Mod mod, FileUploadHeuristic fileType)
        {
            const string FALLBACK_URL = "https://cdn.shibe.online/shibes/f75268ccba1856ad9bab97b4dd332e3a5d1c4a9a.jpg";
            DiscordEmbedBuilder deb = new()
            {
                Author = new()
                {
                    Url = mod.SubmittedBy?.ProfileUrl?.ToString() ?? FALLBACK_URL,
                    Name = mod.SubmittedBy is not null ? $"{mod.SubmittedBy.Username} (ID: {mod.SubmittedBy.NameId})" : "??? (Mod.io API is fantastic and reliable)",
                },
                Description = "Mod files has/have: " + fileType.ToString(),
                Title = $"{mod.Name} (ID: {mod.NameId})",
                Url = mod.ProfileUrl?.ToString() ?? FALLBACK_URL
            };
            foreach (DiscordChannel channel in malformedFileChannels)
            {
                await channel.SendMessageAsync(deb.Build());
            }
        }

        private static DiscordEmbedBuilder CreateEmbed(Mod mod)
        {
            string? image = mod.Logo?.Thumb640x360?.OriginalString ?? mod.Media.Images.FirstOrDefault()?.Original?.ToString();
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

        static void CreateDirectoryRecursive(string? directory)
        {
            if (Directory.Exists(directory))
                return;
            CreateDirectoryRecursive(Path.GetDirectoryName(directory));
        }

        static void FallbackGetChannels()
        {
            // have to do this because dsharpplus doesnt fire guilddownloadscompleted (or whatever the event is called) on .NET 7, for whatever reason
            Logger.Put("The list of mod upload announcement channels is empty! Attempting to rectify this now.", Logger.Reason.Debug);

            GetChannelsFromGuilds(Bot.CurrClient.Guilds);
        }

    }
}

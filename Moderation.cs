using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.Entities.AuditLogs;
using DSharpPlus.EventArgs;
using DSharpPlus.Exceptions;
using DSharpPlus.Interactivity.Extensions;
using Modio.Models;
using System;
using System.ComponentModel;
using System.Reflection;
using System.Text;
using System.Threading.Channels;
using static Voidway_Bot.VoidwayModerationData;

namespace Voidway_Bot {
    internal static class Moderation {
        private enum OpenAiModerationAction
        {
            [Description("Dismiss as false positive")]
            DISMISS_FALSE_POS,
            [Description("Dismiss & ignore alerts from this user for an hour")]
            DISMISS_IGNORE_CHANNEL_1H,
            [Description("Dismiss & ignore alerts from this channel for an hour")]
            DISMISS_IGNORE_USER_1H,
            //[Description("1h mute user")]
            //TIMEOUT_1H,
            //[Description("1d mute user")]
            //TIMEOUT_1D,
            //[Description("3d mute user")]
            //TIMEOUT_3D,
            //[Description("1wk mute user")]
            //TIMEOUT_1WK,
            //[Description("Kick user")]
            //KICK,
            //[Description("Ban user")]
            //BAN,
            //[Description("Ban user (Del last day of msgs)")]
            //BAN_DEL_1D,
        }

        private const string BUTTON_WARN_AUDITLOG_REASON = "voidway.warn.al";
        private const string BUTTON_WARN_PROVIDE_REASON = "voidway.warn.spec";
        private const string BUTTON_WARN_IGNORE = "voidway.warn.dismiss";
        private const string DROPDOWN_OAI_ACTIONTYPE = "voidway.oai.actiontype";
        private const string BUTTON_OAI_TAKEACTION_DONTDELETE = "voidway.oai.takeaction.dontdelete";
        private const string BUTTON_OAI_TAKEACTION_DELETE = "voidway.oai.takeaction.delete";
        static HttpClient clint = new();

        static Dictionary<DiscordChannel, DateTime> ignoreOaiInChannelsUntilAfter = new();
        static Dictionary<DiscordUser, DateTime> ignoreOaiFromUsersUntilAfter = new();

        internal static void HandleModeration(DiscordClient discord) {
            Logger.Put("Adding handlers for moderation events", Logger.Reason.Trace);
            discord.GuildMemberRemoved += (client, e) => { KickHandler(e); return Task.CompletedTask; }; // "An event handler for GUILD_MEMBER_REMOVED took too long to execute" GOD DAMN I DO NOT CAAAARREEEEEEE
            discord.GuildMemberUpdated += (client, e) => { TimeoutHandler(e); return Task.CompletedTask; };
            discord.GuildMemberUpdated += (client, e) => HoistHandler(e.MemberAfter);
            discord.GuildMemberAdded += (client, e) => NewAccountHandler(e);
            discord.GuildMemberAdded += (client, e) => HoistHandler(e.Member);
            discord.GuildBanAdded += (client, e) => BanAddHandler(e);
            discord.GuildBanRemoved += (client, e) => BanRemoveHandler(e);
            discord.MessageDeleted += (client, e) => MessageEmbed(e.Guild, e.Message, "Deleted");
            discord.MessagesBulkDeleted += (client, e) => HandleBulkDeletion(e.Guild, e.Messages);
            discord.MessageDeleted += (client, e) => HandleUserMessageDeleted(e.Guild, e.Message);
            discord.MessageUpdated += (client, e) => MessageEmbed(e.Guild, e.Message, "Edited", e.MessageBefore);
            discord.MessageCreated += (client, e) => HandleUserMessage(e.Author, e.Message);
            discord.MessageCreated += (client, e) => HandleOpenAiModeration(e.Guild, e.Message);
        }

        private static async void TimeoutHandler(GuildMemberUpdateEventArgs e) {
            // if the user's timeout status truly has not changed
            if (e.CommunicationDisabledUntilBefore == e.CommunicationDisabledUntilAfter)
                return;

            DateTime now = DateTime.UtcNow; // discord audit logs use UTC time
            DiscordAuditLogEntry? logEntry = await TryGetAuditLogEntry(e.Guild, dale => dale.ActionType == DiscordAuditLogActionType.MemberUpdate && FuzzyFilterByTime(dale, now));

            DateTimeOffset? timeOutBefore = e.CommunicationDisabledUntilBefore.HasValue && e.CommunicationDisabledUntilBefore > DateTime.Now
                                          ? e.CommunicationDisabledUntilBefore.Value
                                          : null;

            DateTimeOffset? timeOutAfter = e.CommunicationDisabledUntilAfter.HasValue && e.CommunicationDisabledUntilAfter > DateTime.Now
                                         ? e.CommunicationDisabledUntilAfter.Value
                                         : null;

            // means timeout changed
            if (timeOutBefore.HasValue && timeOutAfter.HasValue)
                await ModerationEmbed( // wrap huge call chain
                e.Guild,
                e.Member,
                $"Timeout Changed | Will now end <t:{timeOutAfter.Value.ToUnixTimeSeconds()}:R>",
                logEntry,
                DiscordColor.Cyan,
                "Old end time",
                $"<t:{timeOutBefore.Value.ToUnixTimeSeconds()}:t>",
                false);
            else if (timeOutAfter.HasValue && timeOutAfter > DateTime.Now)
            {
                // just gonna use 'AutoModerationUserCommunicationDisabled' to denote that the user was timed out regardless lol
                await UserWasModerated(e.Member, DiscordAuditLogActionType.AutoModerationUserCommunicationDisabled);
                await ModerationEmbed(e.Guild, e.Member, $"Timed Out. Ends <t:{timeOutAfter.Value.ToUnixTimeSeconds()}:R>", logEntry, DiscordColor.Yellow, actionTriggersWarn: true);
            }
            else if (timeOutBefore.HasValue && !timeOutAfter.HasValue)
                await ModerationEmbed(e.Guild, e.Member, $"Timeout Removed", logEntry, DiscordColor.Cyan);
        }

        private static async void KickHandler(GuildMemberRemoveEventArgs e) {
            DateTime now = DateTime.UtcNow;
            DiscordAuditLogEntry? kickEntry = await TryGetAuditLogEntry(e.Guild, dale => dale.ActionType == DiscordAuditLogActionType.Kick);
            Logger.Put($"{e.Guild.Name}, {e.Member.Username}, ke==null:{kickEntry is null}", Logger.Reason.Trace);
            if (kickEntry is null) return;
            if (!FuzzyFilterByTime(kickEntry, now, 10 * 1000))
            {
                Logger.Put($"User seems to have left, not been kicked (audit log kick entry is over 10sec old) {e.Member.Username}#{e.Member.Discriminator} ({e.Member.Id})");
                return;
            }

            await UserWasModerated(e.Member, DiscordAuditLogActionType.Kick);
            await ModerationEmbed(e.Guild, e.Member, "Kicked", kickEntry, DiscordColor.Orange, actionTriggersWarn: true);
        }

        private static async Task NewAccountHandler(GuildMemberAddEventArgs e)
        {
            // used Math.Abs here because i couldnt be fucked to figure out the right subtraction order lol
            double daysOld = Math.Abs(e.Member.CreationTimestamp.Subtract(DateTime.UtcNow).TotalDays);
            double hoursBetweenCreationAndJoin = Math.Abs(e.Member.JoinedAt.Subtract(e.Member.CreationTimestamp).TotalHours);
            ulong logChannel = Config.FetchNewAccountLogChannel(e.Guild.Id);

            if (logChannel is not 0 && daysOld < 1 && hoursBetweenCreationAndJoin < 1)
                await e.Guild.Channels[logChannel].SendMessageAsync($"Member <@{e.Member.Id}> ({e.Member.Username}#{e.Member.Discriminator}) has a new account & joined recently after creation.");
        }

        private static async Task BanAddHandler(GuildBanAddEventArgs e)
        {
            DiscordAuditLogEntry? banEntry = await TryGetAuditLogEntry(e.Guild, dale => dale.ActionType == DiscordAuditLogActionType.Ban);
            await ModerationEmbed(e.Guild, e.Member, "Banned", banEntry, DiscordColor.Red, "Info pre-ban", PersistentData.GetModerationInfoFor(e.Guild.Id, e.Member.Id), true);
            if (PersistentData.GapDays.Count < 15)
            {
                if (!PersistentData.values.observedMessages.TryGetValue(e.Guild.Id, out var userDict) || !userDict.TryGetValue(e.Member.Id, out var messagesDict))
                {
                    // if there were no messages from that user - assumed to be banned for scamming/TOS age or something
                    return;
                }

                if (messagesDict.Values.Sum(ush => ush) < 100)
                {
                    // less than 100 messages - assumed to be banned for scamming/TOS age or other blatant shit
                    return;
                }

                if (!PersistentData.values.moderationActions.TryGetValue(e.Guild.Id, out var usersDict) || !PersistentData.values.moderationActions.TryGetValue(e.Member.Id, out var actionsDict))
                {
                    await ModerationEmbed(e.Guild, e.Member, "Possibly Banned In Error", null, DiscordColor.Orange, "Reconsider Action Taken", $"It seems this user hasn't been moderated in the past month. Are you sure you wanted to ban this user instead of just muting them? Full user info below.\n{PersistentData.GetModerationInfoFor(e.Guild.Id, e.Member.Id)}", false);
                    return;
                }

                int nonMsgDelActions = actionsDict.Values.SelectMany(v => v.Values).Count(alr => alr != DiscordAuditLogActionType.MessageDelete);
                if (nonMsgDelActions > 2 && PersistentData.TrackedDays.Count() > PersistentData.TRACK_PAST_DAYS / 2)
                    await ModerationEmbed(e.Guild, e.Member, "Possibly Banned In Error", null, DiscordColor.Orange, "Reconsider Action Taken", $"It seems this user was moderated only {nonMsgDelActions} time(s) (w/o msg deletions) in the past month. Full user info below.\n{PersistentData.GetModerationInfoFor(e.Guild.Id, e.Member.Id)}", false);
            }

        }

        private static async Task BanRemoveHandler(GuildBanRemoveEventArgs e)
        {
            DiscordAuditLogEntry? unbanEntry = await TryGetAuditLogEntry(e.Guild, dale => dale.ActionType == DiscordAuditLogActionType.Unban);
            await ModerationEmbed(e.Guild, e.Member, "Ban Removed", unbanEntry, DiscordColor.Green);
        }

        internal static async Task HoistHandler(DiscordMember m)
        {
            bool isHoistServer = Config.IsHoistServer(m.Guild.Id);
            bool needsHoist = Config.IsHoistMember(m.DisplayName[0]);

            if (isHoistServer && needsHoist)
            {
                // try-catch in case an admin does some shit like "#CANADAFOREVER" (in which case they should not be admin because canada)
                try
                {
                    await m.ModifyAsync(mem => { mem.Nickname = "hoist"; mem.AuditLogReason = $"Hoist: name started w/ {m.DisplayName[0]}"; });
                    Logger.Put($"Hoisted {m.Username}#{m.Discriminator} ({m.Id}) for display name {m.DisplayName} in {m.Guild.Name}");
                }
                catch (DiscordException dex)
                {
                    Logger.Warn($"Failed to hoist {m.Username}#{m.Discriminator} ({m.Id}) for display name {m.DisplayName} in {m.Guild.Name}\n\t" + dex.ToString());
                }
            }
        }

        private static async Task ModerationEmbed(DiscordGuild guild, DiscordMember victim, string actionType, DiscordAuditLogEntry? logEntry, DiscordColor color, string customFieldTitle = "", string customField = "", bool actionTriggersWarn = false) {
            VoidwayModerationData timeoutData = GetDataFromReason(logEntry);
            //(string loggedMod, string reason, bool targetNotified) = GetDataFromReason(logEntry);
            DiscordEmbedBuilder embed = new() {
                Title = $"User {actionType}",
                Color = color,
                Footer = new DiscordEmbedBuilder.EmbedFooter() { Text = $"User: {victim.Username} ({victim.Id})" }
            };
            DiscordMessageBuilder dmb = new();

            embed.AddField("User", $"<@{victim.Id}>", true);
            if (!string.IsNullOrWhiteSpace(timeoutData.ModeratorName))
                embed.AddField("Moderator", timeoutData.ModeratorName, true);
            if (!string.IsNullOrWhiteSpace(timeoutData.OriginalReason))
                embed.AddField("Reason", timeoutData.OriginalReason, true);
            if (!string.IsNullOrWhiteSpace(customFieldTitle))
                embed.AddField(customFieldTitle, customField, false);

            bool stillInServer = false;
            try
            {
                await guild.GetMemberAsync(victim.Id, true);
                stillInServer = true;
            }
            catch { }

            if (actionTriggersWarn)
            {
                string footerAddendum = timeoutData.TargetWarnStatus switch
                {
                    TargetNotificationStatus.NOT_ATTEMPTED => "",
                    TargetNotificationStatus.SUCCESS => "\nAlready warned.",
                    TargetNotificationStatus.FAILURE => "\nWarn Failed. Likely strict privacy settings or left server.",
                    TargetNotificationStatus.NOT_APPLICABLE => "\nWarning not applicable.",
                    _ => "\nUnknown if warned or not.",
                };

                embed.Footer.Text = embed.Footer.Text + footerAddendum;

                if (stillInServer && timeoutData.TargetWarnStatus == TargetNotificationStatus.NOT_ATTEMPTED)
                {
                    DiscordButtonComponent buttonSendLogReason = new(ButtonStyle.Primary, BUTTON_WARN_AUDITLOG_REASON, "Send Warning (Log Reason)");
                    DiscordButtonComponent buttonSendCraftedReason = new(ButtonStyle.Secondary, BUTTON_WARN_PROVIDE_REASON, "Send Warning (Custom Reason)");
                    DiscordButtonComponent buttonDismiss = new(ButtonStyle.Danger, BUTTON_WARN_IGNORE, "Dismiss");
                    //DiscordActionRowComponent row = new(new DiscordButtonComponent[] { buttonSendLogReason, buttonSendCraftedReason, buttonDismiss });
                    dmb.AddComponents(buttonSendLogReason, buttonSendCraftedReason, buttonDismiss);
                }
            }

            if (logEntry is null) Logger.Put("Moderation embed created with nonexistent log entry! See below.", Logger.Reason.Warn);
            Logger.Put($"{actionType} {victim.Username}#{victim.Discriminator} ({victim.Id}) in '{guild.Name}' by {timeoutData.ModeratorName} (Audit log says: {logEntry?.UserResponsible?.Username ?? "Unknown"})");

            dmb.AddEmbed(embed);
            var msg = await guild.GetChannel(Config.FetchModerationChannel(guild.Id)).SendMessageAsync(dmb);

            if (dmb.Components.Count > 0)
                HandleWarnButtons(timeoutData, victim, msg);
        }

        static async Task HandleBulkDeletion(DiscordGuild guild, IReadOnlyList<DiscordMessage> messages)
        {
            if (messages.Count == 0)
                return;

            bool sameChannel = messages.All(m => m.Channel! == messages[0].Channel!);
            Logger.Put($"{messages.Count} messages bulk deleted. {(sameChannel ? "In " + messages[0].Channel : "From varying channels, so someone was probably banned.")}");
            foreach (DiscordMessage msg in messages)
            {
                await MessageEmbed(guild, msg, "Bulk Deleted");
            }
        }

        private static async Task MessageEmbed(DiscordGuild guild, DiscordMessage message, string actionType, DiscordMessage? pastMessage = null) {
            if (message.Author is null)
                return;

            if (message.Author.IsBot)
                return;

            if (pastMessage is not null && pastMessage.Content == message.Content)
                return;

            List<string> attachmentsToGet = new();
            string attachments = "";
            foreach (DiscordAttachment attachment in message.Attachments)
            {
                if (attachment.FileSize < 8 * 1024 * 1024) // 8mb limit
                {
                    attachmentsToGet.Add(attachment.ProxyUrl ?? attachment.Url!);
                    attachments += $"\n(Attached) {attachment.Url}";
                }
                else
                {
                    attachments += $"\n{attachment.Url}";
                }
            }

            string desc = !string.IsNullOrEmpty(message.Content) ? "**Message**\n" + message.Content : "";
            if (pastMessage is not null)
                desc += "\n\n**Past Message**\n" + pastMessage.Content;

            if (!string.IsNullOrEmpty(attachments))
                desc += "\n\n**Attachments**" + attachments;

            DiscordEmbedBuilder embed = new() {
                Title = $"Message {actionType}",
                Color = DiscordColor.Gray,
                Description = desc.Trim(),
                Footer = new DiscordEmbedBuilder.EmbedFooter() { Text = $"User: {message.Author.Username} ({message.Author.Id})\nMessage ID: {message.Id}" }
            };
            embed.AddField("User", $"<@{message.Author.Id}>", true)
                 .AddField("Channel", $"<#{message.Channel!.Id}>", true)
                 .AddField("Original Time", $"<t:{message.Timestamp.ToUnixTimeSeconds()}:f>", true);

            ulong channelId = Config.FetchMessagesChannel(guild.Id);
            DiscordChannel? channel = guild.GetChannel(channelId);
            if (channel is null)
            {
                Logger.Warn($"Recieved msg edit/delete event in {guild}, but there is no message channel set in settings/none was found! Cfg says ID = {channelId}.");
                return;
            }

            var dmb = new DiscordMessageBuilder()
                .AddEmbed(embed);

            try
            {
                foreach (string url in attachmentsToGet)
                {
                    Stream attachmentStream = await clint.GetStreamAsync(url);
                    string filename = Path.GetFileName(url).Split('?')[0]; // handle discord's authenticated cdn links
                    dmb.AddFile("SPOILER_" + filename, attachmentStream);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("Exception while adding attachments to msg logger!", ex);
            }

            await channel.SendMessageAsync(dmb);
        }


        private static async Task<DiscordAuditLogEntry?> TryGetAuditLogEntry(DiscordGuild guild, Func<DiscordAuditLogEntry, bool> predicate)
        {
            //AuditLogActionType? alat = (AuditLogActionType?)auditLogType;
            //await Task.Delay(250);

            int _iteration = 0;
            try
            {
                for (int i = 1; i < Config.GetAuditLogRetryCount() + 1; i++)
                {
                    _iteration++;
                    await foreach (DiscordAuditLogEntry auditLogEntry in guild.GetAuditLogsAsync(i/*, null, alat*/))
                    {
                        if (predicate(auditLogEntry)) return auditLogEntry;
                    }

                    await Task.Delay(1000 * i); // progressively increase delay to avoid setting off ratelimiting
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"UNEXPLAINABLE EXCEPTION WHILE ATTEMPTING TO GET AUDIT LOG ENTRY ON ITERATION {_iteration}!!!", ex);
            }

            return null;
        }


        private static bool FuzzyFilterByTime(DiscordAuditLogEntry logEntry, DateTime currTime, int fuzzByMs = 5000)
        {
            //TimeSpan fuzzBy = TimeSpan.FromMilliseconds(fuzzByMs);
            int msDiff = (int)Math.Abs(logEntry.CreationTimestamp.DateTime.Subtract(currTime).TotalMilliseconds);
            bool ret = msDiff < fuzzByMs;
            // might be better to make a VS2022 debug logpoint but oh well
            Logger.Put($"logentry creation: {logEntry.CreationTimestamp.DateTime:h:mm:ss:t}; time: {currTime:h:mm:ss:t}; diff={msDiff}ms; fuzzby: {fuzzByMs}ms; RET={ret}", Logger.Reason.Debug);
            return ret;
        }

        private static VoidwayModerationData GetDataFromReason(DiscordAuditLogEntry? logEntry)
        {
            DiscordUser? userResponsible = logEntry?.UserResponsible;
            if (logEntry is null || userResponsible is null) return new("*Unknown*", logEntry?.Reason ?? "*Unknown*", TargetNotificationStatus.UNKNOWN);
            else if (userResponsible != Bot.CurrUser) return new(logEntry.Reason ?? "*Unknown*", userResponsible.Username, TargetNotificationStatus.NOT_ATTEMPTED);

            // now handle the case that the bot took action

            if (SlashCommands.WasByBotCommand(logEntry.Reason, out var timeoutData)) {
                return timeoutData;
            }
            else return new VoidwayModerationData(logEntry.Reason ?? "*Unknown*", userResponsible.Username, TargetNotificationStatus.NOT_ATTEMPTED);
        }

        private static async void HandleWarnButtons(VoidwayModerationData timeoutData, DiscordMember timedOutUser, DiscordMessage waitOnMsg)
        {
            try
            {
                var res = await waitOnMsg.WaitForButtonAsync(TimeSpan.FromMinutes(5));

                if (res.TimedOut)
                {
                    await RemoveInteractionComponents(waitOnMsg);
                    return;
                }


                string footer = waitOnMsg.Embeds[0].Footer!.Text!;

                switch (res.Result.Id)
                {
                    case BUTTON_WARN_AUDITLOG_REASON:
                        await res.Result.Interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, new() { Content = "Starting interaction!", IsEphemeral = true });
                        DiscordMessage alFollowup = await res.Result.Channel.SendMessageAsync($"Sending warning message to {timedOutUser.Username}...");
                        bool alWarnSuccess = await SendWarningMessage(timedOutUser, "muted", timeoutData.OriginalReason, waitOnMsg.Channel!.Guild.Name);
                        if (alWarnSuccess)
                            await alFollowup.ModifyAsync(alFollowup.Content + " Success!");
                        else
                            await alFollowup.ModifyAsync(alFollowup.Content + " Failed! Could be due to them leaving or their privacy settings!");

                        footer = footer.Split('\n')[0] + "\nWarned for this interaction.";
                        await RemoveInteractionComponents(waitOnMsg, footer);
                        break;
                    case BUTTON_WARN_PROVIDE_REASON:
                        await res.Result.Interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, new() { Content = "Starting interaction!", IsEphemeral = true });
                        DiscordMessage askWarnTxtMsg = await res.Result.Channel.SendMessageAsync($"{res.Result.User.Username}, send a message of what you want the reason to be in the warn message, instead of '{timeoutData.OriginalReason}'");
                        TimeSpan timeoutSpan = TimeSpan.FromMinutes(1);
                        var nextMsgTask = waitOnMsg.Channel!.GetNextMessageAsync(m => m.Author! == res.Result.User);
                        await Task.WhenAny(Task.Delay(timeoutSpan - TimeSpan.FromSeconds(10)), nextMsgTask);

                        if (!nextMsgTask.IsCompleted)
                        {
                            await askWarnTxtMsg.ModifyAsync(askWarnTxtMsg.Content + $", **10 seconds left!**");
                        }

                        var nextMsgRes = await nextMsgTask;
                        if (nextMsgRes.TimedOut)
                        {
                            TimeSpan deleteOffset = TimeSpan.FromSeconds(60);
                            DateTimeOffset deleteTime = DateTime.Now + deleteOffset;
                            await askWarnTxtMsg.ModifyAsync($"Timed out getting warning reason. This message will be deleted <t:{deleteTime.ToUnixTimeSeconds()}:R>");
                            HandleWarnButtons(timeoutData, timedOutUser, waitOnMsg);
                            await Task.Delay(deleteOffset);
                            await askWarnTxtMsg.DeleteAsync();
                            return;
                        }

                        DiscordMessage warnMsg = nextMsgRes.Result;

                        bool warnCustomTxtRes = await SendWarningMessage(timedOutUser, "muted", warnMsg.Content ?? "<No Content>", waitOnMsg.Channel!.Guild.Name);

                        await askWarnTxtMsg.ModifyAsync(warnCustomTxtRes ? $"Successfully warned {timedOutUser.Username} with that reason!" : $"Failed to warn {timedOutUser.Username} - their privacy settings may be too strict or they may have left.");

						footer = footer.Split('\n')[0] + "\nWarned for this interaction.";
						await RemoveInteractionComponents(waitOnMsg, footer);
                        break;
                    case BUTTON_WARN_IGNORE:

                        footer = footer.Split(',')[0] + " | Not warned (Dismissed).";
                        await RemoveInteractionComponents(waitOnMsg);
                        await res.Result.Interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, new() { Content = "Dismissed!", IsEphemeral = true });
                        break;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Exception while waitinng for warn buttons to be clicked!", ex);
            }
        }

        private static async Task RemoveInteractionComponents(DiscordMessage removeComponents, string? changeFooterTo = null)
        {
            DiscordMessageBuilder dmb = new(removeComponents);

            dmb.ClearComponents();

            if (!string.IsNullOrEmpty(changeFooterTo))
            {
                DiscordEmbedBuilder deb = new(dmb.Embeds[0]);
                deb.WithFooter(changeFooterTo);
                dmb.ClearEmbeds();
                dmb.AddEmbed(deb);
            }

            await removeComponents.ModifyAsync(dmb);
        }

        internal static async Task<bool> SendWarningMessage(DiscordMember member, string action, string reason, string serverName)
        {
            try
            {
                await member.SendMessageAsync($"The moderators in **{serverName}** have {action} you for the following reason:\n\"{reason}\"\n\n*This bot does not relay messages to the staff of that server. If you want clarification, message a moderator / admin in that server.*");

                return true;
            }
            catch (DiscordException dx)
            {
                Logger.Warn($"Exception when trying to DM {member.Username} a timeout reason/warning", dx);
                return false;
            }
        }

        private static async Task HandleUserMessageDeleted(DiscordGuild guild, DiscordMessage msg)
        {
            if (guild is null || msg.Channel!.IsPrivate)
                return;

            // if theres no audit log entry then the user deleted the message on their own
            if (await TryGetAuditLogEntry(guild, ale => ale.ActionType == DiscordAuditLogActionType.MessageDelete) is null)
                return;

            if (msg.Author is not DiscordMember member)
                member = await guild.GetMemberAsync(msg.Author!.Id);

            await UserWasModerated(member, DiscordAuditLogActionType.MessageDelete);
        }

        private static Task HandleUserMessage(DiscordUser author, DiscordMessage message)
        {
            if (message.Channel!.IsPrivate)
                return Task.CompletedTask;

            if (!PersistentData.values.observedMessages.TryGetValue(message.Channel.Guild.Id, out var userDict))
            {
                userDict = new();
                PersistentData.values.observedMessages[message.Channel.Guild.Id] = userDict;
            }

            if (!userDict.TryGetValue(message.Author!.Id, out var calendarDict))
            {
                calendarDict = new();
                userDict[message.Author.Id] = calendarDict;
            }

            DateTimeOffset creation = message.CreationTimestamp;
            DateOnly date = new(creation.Year, creation.Month, creation.Day);
            calendarDict.TryGetValue(date, out ushort msgCount);
            msgCount++;
            calendarDict[date] = msgCount;

            return Task.Run(() => { PersistentData.TrimOldMessages(); PersistentData.WritePersistentData(); });
        }

        private static Task UserWasModerated(DiscordMember member, DiscordAuditLogActionType actionType)
        {
            if (!PersistentData.values.moderationActions.TryGetValue(member.Guild.Id, out var userDict))
            {
                userDict = new();
                PersistentData.values.moderationActions[member.Guild.Id] = userDict;
            }

            if (!userDict.TryGetValue(member.Id, out var moderationDict))
            {
                moderationDict = new();
                userDict[member.Id] = moderationDict;
            }

            moderationDict[DateTime.Now] = actionType;

            return Task.Run(() => { PersistentData.TrimOldModerations(); PersistentData.WritePersistentData(); });
        }



        private static async Task HandleOpenAiModeration(DiscordGuild guild, DiscordMessage message)
        {
            if (message.Author!.IsBot || message.Channel!.IsPrivate) return;
            DiscordMember? memb = message.Author as DiscordMember;
            try
            {
                memb ??= await guild.GetMemberAsync(message.Author.Id);
            }
            catch { }
            if (memb is null) return;
            if (memb.Permissions.HasPermission(Permissions.ManageMessages)) return;
            if (Bot.OpenAiClient is null || !Config.IsModeratingDiscordMessages() || Config.IsExemptFromOpenAiScanning(message.Channel.Id)) return;
            if (ignoreOaiFromUsersUntilAfter.TryGetValue(memb, out DateTime ignoreTime) && DateTime.Now < ignoreTime) return;
            if (ignoreOaiInChannelsUntilAfter.TryGetValue(message.Channel, out ignoreTime) && DateTime.Now < ignoreTime) return;

            string msgStr = message.ReferencedMessage is not null && string.IsNullOrEmpty(message.ReferencedMessage.Content)
                          ? $"{message.Author.GlobalName} (replying to \"{message.ReferencedMessage.Content}\"): {message.Content}"
                          : $"{message.Author.GlobalName}: {message.Content}";
            var moderationRes = await Bot.OpenAiClient.Moderation.CallModerationAsync(msgStr);

            string messageTooLongStr = "... (truncated by bot)";

            var res = moderationRes.Results.FirstOrDefault();
            if (res is null) return;

#if DEBUG
            Logger.Put($"Message from {message.Author.Username} in {message.Channel.Name} was {(res.Flagged ? "flagged" : "not flagged")}. Highest score was {Math.Round(res.HighestFlagScore * 100, 2)}% for {res.CategoryScores.Max(kvp => kvp.Key)}", Logger.Reason.Debug);
#endif

            if (!res.Flagged) return;

            StringBuilder sb = new($"A message");
            if (memb is not null)
                sb.Append($" from **<@{memb.Id}>** ({memb.Username})");
            else
                sb.Append($" from {message.Author.Username}");

            sb.AppendLine($" in the channel **{message.Channel.Name}** has been flagged by OpenAI in the following categories: **");

            foreach (var flagged in res.FlaggedCategories)
            {
                sb.Append(flagged);
                sb.Append("**, **");
            }

            sb.Remove(sb.Length - 6, 6);
            sb.Append("**");

            sb.AppendLine();
            sb.Append("For the following content: **");
            if (message.Content!.Length < 100)
            {
                sb.Append(message.Content.Replace("*", "\\*"));
                sb.Append("**");
            }
            else
            {
                sb.Append(message.Content[..100].Replace("*", "\\*"));
                sb.Append("**");
                sb.Append(messageTooLongStr);
            }

            sb.AppendLine();
            sb.Append("Jump: ");
            sb.Append(message.JumpLink);

            ulong logChId = Config.FetchOpenAiModerationChannel(guild.Id);
            if (logChId == default) return;

            if (guild.Channels.TryGetValue(logChId, out DiscordChannel? channel))
            {
                // SO cludgy and SO slow
                var components = from modAction in typeof(OpenAiModerationAction).GetFields()
                                 where modAction.GetCustomAttribute<DescriptionAttribute>() is not null
                                 select new DiscordSelectComponentOption(modAction.GetCustomAttribute<DescriptionAttribute>()!.Description, modAction.Name);


                DiscordSelectComponent dropdown = new(DROPDOWN_OAI_ACTIONTYPE, "Select an action to take", components);

                var dmb = new DiscordMessageBuilder()
                    .WithContent(sb.ToString())
                    .AddComponents(dropdown);

                DiscordMessage modMsg = await channel.SendMessageAsync(dmb);

                HandleOpenAiModerationSelection(message, modMsg);
            }
            else
                Logger.Warn($"OpenAI channel for {guild.Name} (ID {guild.Id}) not found! That server doesn't have a channel with the given ID {logChId}");
        }

        private static async void HandleOpenAiModerationSelection(DiscordMessage flaggedMsg, DiscordMessage waitOnMsg)
        {
            try
            {
                var res = await waitOnMsg.WaitForSelectAsync(DROPDOWN_OAI_ACTIONTYPE, TimeSpan.FromMinutes(15));
                
                if (res.TimedOut)
                {
                    await RemoveInteractionComponents(waitOnMsg);
                    return;
                }

                if (res.Result.Interaction is null)
                {
                    await RemoveInteractionComponents(waitOnMsg);
                    return;
                }

                if (!Enum.TryParse<OpenAiModerationAction>(res.Result.Values.Single(), out var selectedAction))
                {
                    Logger.Put($"Got an unparsable dropdown option. What? Dropdown result: " + res.Result.Values.Single());
                    await RemoveInteractionComponents(waitOnMsg);
                    return;
                }

                await RemoveInteractionComponents(waitOnMsg);
                
                switch (selectedAction)
                {
                    case OpenAiModerationAction.DISMISS_FALSE_POS:
                        await res.Result.Interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, new() { Content = "Dismissing warning, editing message!", IsEphemeral = true });
                        await waitOnMsg.ModifyAsync("This warning was a false positive.");
                        break;
                    case OpenAiModerationAction.DISMISS_IGNORE_CHANNEL_1H:
                        await res.Result.Interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, new() { Content = "Dismissing warning, editing message!", IsEphemeral = true });
                        await waitOnMsg.ModifyAsync($"This warning was a false positive. Ignoring warnings from <#{flaggedMsg.Channel!.Id}> for an hour.");
                        ignoreOaiInChannelsUntilAfter[flaggedMsg.Channel] = DateTime.Now.AddHours(1);
                        break;
                    case OpenAiModerationAction.DISMISS_IGNORE_USER_1H:
                        await res.Result.Interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, new() { Content = "Dismissing warning, editing message!", IsEphemeral = true });
                        await waitOnMsg.ModifyAsync($"This warning was a false positive. Ignoring warnings from <@{flaggedMsg.Author!.Id}> for an hour.");
                        ignoreOaiFromUsersUntilAfter[flaggedMsg.Author] = DateTime.Now.AddHours(1);
                        break;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("Exception while handling OpenAI moderation selection!", ex);
            }
        }
    }
}

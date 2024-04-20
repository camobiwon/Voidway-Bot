using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.Exceptions;
using DSharpPlus.Interactivity.Extensions;
using DSharpPlus.SlashCommands;
using DSharpPlus.SlashCommands.Attributes;
using Modio.Models;
using System.Threading;

namespace Voidway_Bot
{
    // info on making a slash command is here. why? i dunno. https://github.com/DSharpPlus/DSharpPlus/tree/master/DSharpPlus.SlashCommands
    internal class ContextActions : ApplicationCommandModule
    {
        internal static Dictionary<ulong, TaskCompletionSource<ModalSubmitEventArgs>> modalsWaitingForCompletion = new();
        internal const string MODNOTES_MODAL_ID_FORMAT = "voidwaybot.modnotes.{0}"; // original interaction id
        internal const string KICK_MODAL_ID_FORMAT = "voidwaybot.kickmodal.{0}"; // original interaction id

        [ContextMenu(ApplicationCommandType.UserContextMenu, "Hoist", true)]
        [SlashRequirePermissions(Permissions.ManageNicknames, false)]
        public async Task ManualHoist(ContextMenuContext ctx)
        {
            bool targetHasManageNickPerms = ctx.TargetMember.Permissions.HasPermission(Permissions.ManageNicknames);
            await Moderation.HoistHandler(ctx.TargetMember);
            await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, new DiscordInteractionResponseBuilder()
            {
                IsEphemeral = true,
                Content = $"{(targetHasManageNickPerms ? "Probably" : "Successfully")} hoisted {ctx.TargetMember.DisplayName}."
            });
        }

        [ContextMenu(ApplicationCommandType.MessageContextMenu, "Thread Owner: Pin Message", true)]
        [SlashRequireBotPermissions(Permissions.ManageMessages)]
        [SlashRequireThreadOwner]
        public async Task PinThreadMessage(ContextMenuContext ctx)
        {
            if (!Config.GetThreadCreatorPinMessages())
            {
                await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, new DiscordInteractionResponseBuilder()
                {
                    IsEphemeral = true,
                    Content = "Thread creators aren't permitted to pin messages in their threads. Ask a moderator to pin that message.",
                });
                return;
            }

            //DiscordThreadChannelMember threadCreator = thread.user.OrderBy(tm => tm.JoinedAt).First();

            string res = "Pinned!";

            try
            {
                await ctx.TargetMessage.PinAsync();
            }
            catch (DiscordException ex)
            {
                DiscordUser author = ctx.TargetMessage!.Author!;
                res = $"Unable to pin that message. Tell the developer: {ex.GetType().FullName}";
                Logger.Error
                    (
                        $"Unable to pin message '{Logger.EnsureShorterThan(ctx.TargetMessage!.Content!, 50)}' by {author.Username}#{author.Discriminator} in #{ctx.Channel.Parent?.Name ?? "<NOPARENT>"}->'{ctx.Channel.Name}'\n\t" +
                        $"(AuthorId={author.Id}; MsgId={ctx.TargetMessage.Id}; ReqBy {ctx.User.Username}#{ctx.User.Discriminator} Id={ctx.User.Id}, ThreadId={ctx.Channel.Id})",
                        ex
                    );
            }

            await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, new DiscordInteractionResponseBuilder()
            {
                IsEphemeral = true,
                Content = res
            });
        }

        [ContextMenu(ApplicationCommandType.MessageContextMenu, "Thread Owner: Unpin Message", true)]
        [SlashRequireBotPermissions(Permissions.ManageMessages)]
        [SlashRequireThreadOwner]
        public async Task UnpinThreadMessage(ContextMenuContext ctx)
        {
            if (!Config.GetThreadCreatorPinMessages())
            {
                await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, new DiscordInteractionResponseBuilder()
                {
                    IsEphemeral = true,
                    Content = "Thread creators aren't permitted to pin/unpin messages in their threads. Ask a moderator to pin that message.",
                });
                return;
            }

            //DiscordThreadChannelMember threadCreator = thread.user.OrderBy(tm => tm.JoinedAt).First();

            string res = "Unpinned!";

            try
            {
                await ctx.TargetMessage.UnpinAsync();
            }
            catch (DiscordException ex)
            {
                DiscordUser author = ctx.TargetMessage!.Author!;
                res = $"Unable to unpin that message. Tell the developer: {ex.GetType().FullName}";
                Logger.Error
                (
                $"Unable to unpin message '{Logger.EnsureShorterThan(ctx.TargetMessage!.Content!, 50)}' by {author.Username}#{author.Discriminator} in #{ctx.Channel.Parent?.Name ?? "<NOPARENT>"}->'{ctx.Channel.Name}'\n\t" +
                $"(AuthorId={author.Id}; MsgId={ctx.TargetMessage.Id}; ReqBy {ctx.User.Username}#{ctx.User.Discriminator} Id={ctx.User.Id}, ThreadId={ctx.Channel.Id})",
                        ex
                    );
            }

            await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, new DiscordInteractionResponseBuilder()
            {
                IsEphemeral = true,
                Content = res
            });
        }

        [ContextMenu(ApplicationCommandType.MessageContextMenu, "Thread Owner: Delete Message", true)]
        [SlashRequireBotPermissions(Permissions.ManageMessages)]
        [SlashRequireThreadOwner]
        public async Task DeleteThreadMessage(ContextMenuContext ctx)
        {
            if (!Config.GetThreadCreatorDeleteMessages())
            {
                await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, new DiscordInteractionResponseBuilder()
                {
                    IsEphemeral = true,
                    Content = "Thread creators aren't permitted to delete messages in their threads. Ask a moderator to pin that message.",
                });
                return;
            }

            //DiscordThreadChannelMember threadCreator = thread.user.OrderBy(tm => tm.JoinedAt).First();

            string res = "Deleted!";

            try
            {
                await ctx.TargetMessage.DeleteAsync($"Requested by thread owner ({ctx.User.Username}#{ctx.User.Discriminator} {ctx.User.Id})");
            }
            catch (DiscordException ex)
            {
                DiscordUser author = ctx.TargetMessage!.Author!;
                res = $"Unable to delete that message. Tell the developer: {ex.GetType().FullName}";
                Logger.Error
                    (
                        $"Unable to delete message '{Logger.EnsureShorterThan(ctx.TargetMessage!.Content!, 50)}' by {author.Username}#{author.Discriminator} in #{ctx.Channel.Parent?.Name ?? "<NOPARENT>"}->'{ctx.Channel.Name}'\n\t" +
                        $"(AuthorId={author.Id}; MsgId={ctx.TargetMessage.Id}; ReqBy {ctx.User.Username}#{ctx.User.Discriminator} Id={ctx.User.Id}, ThreadId={ctx.Channel.Id})",
                        ex
                    );
            }

            await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, new DiscordInteractionResponseBuilder()
            {
                IsEphemeral = true,
                Content = res
            });
        }

        [ContextMenu(ApplicationCommandType.UserContextMenu, "Mod: Mod Notes", true)]
        [SlashRequireUserPermissions(Permissions.ManageMessages)]
        public async Task CheckModNotes(ContextMenuContext ctx)
        {
            DiscordChannel? noteChannel = await Config.GetModNotesChannel(ctx.Client, ctx.Guild.Id);

            if (noteChannel is null)
            {
                await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, new DiscordInteractionResponseBuilder()
                {
                    IsEphemeral = true,
                    Content = $"No mod notes channel set up for {ctx.Guild.Name}. Ask the operator to set one up, or set it with a config command."
                });
                return;
            }

            DiscordMessage notesMessage = await PersistentData.GetOrCreateModNotesMessage(noteChannel, ctx.TargetUser);
            string[] notesParts = notesMessage.Content?.Split(PersistentData.MOD_NOTE_SPLIT_ON) ?? Array.Empty<string>();
            string modalContent = notesParts.Length < 2 ?
                "" :
                string.Join(PersistentData.MOD_NOTE_SPLIT_ON, notesParts[1..]);

            TaskCompletionSource<ModalSubmitEventArgs> tcs = new();
            modalsWaitingForCompletion.Add(ctx.InteractionId, tcs);

            string modalId = string.Format(MODNOTES_MODAL_ID_FORMAT, ctx.InteractionId);
            //DiscordInteractionResponseBuilder modalBuilder = ModalBuilder.Create(modalId)
            //    .WithContent(modalContent);
            const string MODAL_TEXT_KEY = "text";
            var modalBuilder = new DiscordInteractionResponseBuilder()
                .AsEphemeral(true)
                .WithTitle("Mod notes for " + ctx.TargetUser.Username)
                .WithCustomId(modalId)
                .AddComponents(new TextInputComponent("Notes", MODAL_TEXT_KEY, "Enter mod notes here. Can be multiline.", modalContent, true, TextInputStyle.Paragraph, 0, 1950));

            await ctx.CreateResponseAsync(InteractionResponseType.Modal, modalBuilder);

            await Task.WhenAny(tcs.Task, Task.Delay(Config.GetFilterResponseTimeout() * 1000));

            modalsWaitingForCompletion.Remove(ctx.InteractionId);
            if (!tcs.Task.IsCompleted)
                return;

            //ctx.Client.GetInteractivity().WaitForModalAsync(modalId, TimeSpan.FromSeconds(Config.GetFilterResponseTimeout()))
            ModalSubmitEventArgs modalArgs = tcs.Task.Result;
            string recievedText = modalArgs.Values[MODAL_TEXT_KEY];

            var modalResponseBuilder = new DiscordInteractionResponseBuilder()
                .AsEphemeral(true)
                .WithContent("Updating mod notes...");
            
            await modalArgs.Interaction.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource, modalResponseBuilder);
            await notesMessage.ModifyAsync(string.Format(PersistentData.MOD_NOTE_START, $"<@{ctx.TargetUser!.Id}> ({ctx.TargetMember!.DisplayName})") + recievedText);

            var webhookBuilder = new DiscordWebhookBuilder()
                .WithContent("Updated mod notes!");
            await modalArgs.Interaction.EditOriginalResponseAsync(webhookBuilder);

            Logger.Put($"Updated mod notes for {ctx.TargetUser} in {ctx.Guild}, see below.\n\t{recievedText}");
        }


        [ContextMenu(ApplicationCommandType.UserContextMenu, "Mod: Kick", true)]
        [SlashRequireUserPermissions(Permissions.ManageMessages)]
        public async Task ModalKick(ContextMenuContext ctx)
        {
            if (ctx.TargetMember is null)
            {
                await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, new DiscordInteractionResponseBuilder()
                {
                    IsEphemeral = true,
                    Content = "I can't find that user in the server. They might have left."
                });
                return;
            }

            if (ctx.TargetMember.Permissions.HasPermission(Permissions.Administrator))
            {
                await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, new DiscordInteractionResponseBuilder()
                {
                    IsEphemeral = true,
                    Content = "You can't kick an admin!"
                });
                return;
            }

            TaskCompletionSource<ModalSubmitEventArgs> tcs = new();
            modalsWaitingForCompletion.Add(ctx.InteractionId, tcs);

            string[] funnyThings =
            [
               "they killed my grandma",
               "my dog sniffed them and furrowed his brow",
               "they said my goat was washed",
               //"i asked them where the bathroom was and they couldnt hear what i was saying so eventually after saying what 20 times they just said haha yeah",
               //"they asked if i wanted a ride on their \"magic carpet\" and im not racist but if its anything like the one from aladdin... tbh i dont want sand on my clothes",
               "they gave me a bottle of soju but forbade me from saying the line",
            ];

            string modalId = string.Format(KICK_MODAL_ID_FORMAT, ctx.InteractionId);
            
            const string MODAL_REASON_KEY = "reason";
            const string MODAL_SEND_KEY = "sendTo";
            //const string MODAL_SENDMSG_KEY = "sendMsg";
            //const string MODAL_SENDMSG_YES = "yes";
            //const string MODAL_SENDMSG_NO = "no";
            //DiscordSelectComponentOption[] options =
            //[
            //    new DiscordSelectComponentOption("Send message with reason", MODAL_SENDMSG_YES),
            //    new DiscordSelectComponentOption("Don't send message", MODAL_SENDMSG_NO)
            //];
            var modalBuilder = new DiscordInteractionResponseBuilder()
                .AsEphemeral(true)
                .WithTitle("Kick " + ctx.TargetUser.Username + "?")
                .WithCustomId(modalId)
                // this shit doesnt fucking work unless i give it only a single one, and im this close to losing my shit over it
                .AddComponents(new TextInputComponent("Reason, will be sent to user", MODAL_REASON_KEY, "Leave blank to not send", "", false, TextInputStyle.Paragraph, 0, 1984));
                // fine fuck it .AddComponents(new TextInputComponent("Reason", MODAL_REASON_KEY, "'" + funnyThings[Random.Shared.Next(funnyThings.Length)] + "'", "", true, TextInputStyle.Paragraph, 0, 1984));

            await ctx.CreateResponseAsync(InteractionResponseType.Modal, modalBuilder);

            await Task.WhenAny(tcs.Task, Task.Delay(Config.GetFilterResponseTimeout() * 1000));

            modalsWaitingForCompletion.Remove(ctx.InteractionId);
            if (!tcs.Task.IsCompleted)
                return;

            ModalSubmitEventArgs modalArgs = tcs.Task.Result;
            string kickReason = modalArgs.Values[MODAL_REASON_KEY];
            //bool needsWarn = modalArgs.Values.TryGetValue(MODAL_SEND_KEY, out string? sendToUser);
            bool needsWarn = true; // declare as true in case i ever want to add toggling functionality. thanks discord!

            VoidwayModerationData.TargetNotificationStatus notifStatus = needsWarn 
                ? VoidwayModerationData.TargetNotificationStatus.FAILURE
                : VoidwayModerationData.TargetNotificationStatus.NOT_ATTEMPTED;

            var modalResponseBuilder = new DiscordInteractionResponseBuilder()
                .AsEphemeral(true)
                .WithContent(needsWarn ? "Warning..." : "Kicking...");

            await modalArgs.Interaction.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource, modalResponseBuilder);

            bool warnSuccess = false;
            if (needsWarn)
            {
                warnSuccess = await Moderation.SendWarningMessage(ctx.TargetMember, "kicked", kickReason, ctx.Guild.Name);

                notifStatus = warnSuccess
                    ? VoidwayModerationData.TargetNotificationStatus.SUCCESS
                    : VoidwayModerationData.TargetNotificationStatus.FAILURE;
                var kickWebhookBuilder = new DiscordWebhookBuilder()
                    .WithContent($"{(warnSuccess ? "Successfully warned!" : "Failed to warn, probably left or has DMs off.")} Now kicking...");

                await modalArgs.Interaction.EditOriginalResponseAsync(kickWebhookBuilder);
            }

            string mangledReason = $"By {ctx.User.Username}: " + kickReason;
            SlashCommands.AddModerationPerformedByCommand(mangledReason, new(kickReason, ctx.User.Username, notifStatus));

            bool kickSuccess = false;

            try
            {
                await ctx.TargetMember.RemoveAsync(mangledReason);
                kickSuccess = true;
            }
            catch(Exception ex)
            {
                Logger.Warn($"Failed to kick user {ctx.TargetMember.Username} in {ctx.Guild.Name}", ex);
            }

            var webhookBuilder = new DiscordWebhookBuilder()
                .WithContent(kickSuccess ? "Kicked!" : $"Failed to kick! {(warnSuccess ? "They still got the warning message, so... Try again via the built-in Discord menu?" : "They didn't get warned, so that's the upshot.")}");
            await modalArgs.Interaction.EditOriginalResponseAsync(webhookBuilder);
        }
    }
}

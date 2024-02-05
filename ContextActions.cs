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
        internal const string MODNOTES_MODAL_ID_FORMAT = "voidwaybot.modnotes.{0}";

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

        [ContextMenu(ApplicationCommandType.MessageContextMenu, "Thread owner: Pin message", true)]
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

        [ContextMenu(ApplicationCommandType.MessageContextMenu, "Thread owner: Unpin message", true)]
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

        [ContextMenu(ApplicationCommandType.MessageContextMenu, "Thread owner: Delete message", true)]
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

        [ContextMenu(ApplicationCommandType.UserContextMenu, "Moderation: Check mod notes", true)]
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
    }
}

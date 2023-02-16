using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.Exceptions;
using DSharpPlus.Interactivity.Extensions;
using DSharpPlus.SlashCommands;
using DSharpPlus.SlashCommands.Attributes;
using System.Threading;

namespace Voidway_Bot
{
    // info on making a slash command is here. why? i dunno. https://github.com/DSharpPlus/DSharpPlus/tree/master/DSharpPlus.SlashCommands
    internal class ContextActions : ApplicationCommandModule
    {
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
                DiscordUser author = ctx.TargetMessage.Author;
                res = $"Unable to pin that message. Tell the developer: {ex.GetType().FullName}";
                Logger.Error
                    (
                        $"Unable to pin message '{Logger.EnsureShorterThan(ctx.TargetMessage.Content, 50)}' by {author.Username}#{author.Discriminator} in #{ctx.Channel.Parent?.Name ?? "<NOPARENT>"}->'{ctx.Channel.Name}'\n\t" +
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
                    Content = "Thread creators aren't permitted to unpin messages in their threads. Ask a moderator to pin that message.",
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
                DiscordUser author = ctx.TargetMessage.Author;
                res = $"Unable to unpin that message. Tell the developer: {ex.GetType().FullName}";
                Logger.Error
                (
                $"Unable to unpin message '{Logger.EnsureShorterThan(ctx.TargetMessage.Content, 50)}' by {author.Username}#{author.Discriminator} in #{ctx.Channel.Parent?.Name ?? "<NOPARENT>"}->'{ctx.Channel.Name}'\n\t" +
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
                DiscordUser author = ctx.TargetMessage.Author;
                res = $"Unable to delete that message. Tell the developer: {ex.GetType().FullName}";
                Logger.Error
                    (
                        $"Unable to delete message '{Logger.EnsureShorterThan(ctx.TargetMessage.Content, 50)}' by {author.Username}#{author.Discriminator} in #{ctx.Channel.Parent?.Name ?? "<NOPARENT>"}->'{ctx.Channel.Name}'\n\t" +
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
    }
}

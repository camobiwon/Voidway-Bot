using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.Exceptions;
using DSharpPlus.Interactivity.Extensions;
using DSharpPlus.SlashCommands;
using DSharpPlus.SlashCommands.Attributes;

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

            if (ctx.Channel is not DiscordThreadChannel thread)
            {
                await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, new DiscordInteractionResponseBuilder()
                {
                    IsEphemeral = true,
                    Content = "This isn't a thread channel!",
                });
                return;
            }
            DiscordUser? user = thread.Users.FirstOrDefault(tm => tm.Id == ctx.User.Id);

            if (user is null)
            {
                await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, new DiscordInteractionResponseBuilder()
                {
                    IsEphemeral = true,
                    Content = "You aren't in this thread.... What?",
                });
                return;
            }

            if (user.Id != thread.CreatorId)
            {
                await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, new DiscordInteractionResponseBuilder()
                {
                    IsEphemeral = true,
                    Content = "This isn't your thread!",
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
                        $"Unable to pin message '{Logger.EnsureShorterThan(ctx.TargetMessage.Content, 50)}' by {author.Username}#{author.Discriminator} in #{thread.Parent?.Name ?? "<NOPARENT>"}->'{thread.Name}'\n\t" +
                        $"(AuthorId={author.Id}; MsgId={ctx.TargetMessage.Id}; ReqBy {ctx.User.Username}#{ctx.User.Discriminator} Id={ctx.User.Id}, ThreadId={thread.Id})",
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
        public async Task DeleteThreadMessage(ContextMenuContext ctx)
        {
            if (!Config.GetThreadCreatorPinMessages())
            {
                await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, new DiscordInteractionResponseBuilder()
                {
                    IsEphemeral = true,
                    Content = "Thread creators aren't permitted to delete messages in their threads. Ask a moderator to pin that message.",
                });
                return;
            }

            if (ctx.Channel is not DiscordThreadChannel thread)
            {
                await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, new DiscordInteractionResponseBuilder()
                {
                    IsEphemeral = true,
                    Content = "This isn't a thread channel!",
                });
                return;
            }
            DiscordUser? user = thread.Users.FirstOrDefault(tm => tm.Id == ctx.User.Id);

            if (user is null)
            {
                await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, new DiscordInteractionResponseBuilder()
                {
                    IsEphemeral = true,
                    Content = "You aren't in this thread.... What?",
                });
                return;
            }

            if (user.Id != thread.CreatorId)
            {
                await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, new DiscordInteractionResponseBuilder()
                {
                    IsEphemeral = true,
                    Content = "This isn't your thread!",
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
                res = $"Unable to pin that message. Tell the developer: {ex.GetType().FullName}";
                Logger.Error
                    (
                        $"Unable to delete message '{Logger.EnsureShorterThan(ctx.TargetMessage.Content, 50)}' by {author.Username}#{author.Discriminator} in #{thread.Parent?.Name ?? "<NOPARENT>"}->'{thread.Name}'\n\t" +
                        $"(AuthorId={author.Id}; MsgId={ctx.TargetMessage.Id}; ReqBy {ctx.User.Username}#{ctx.User.Discriminator} Id={ctx.User.Id}, ThreadId={thread.Id})",
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

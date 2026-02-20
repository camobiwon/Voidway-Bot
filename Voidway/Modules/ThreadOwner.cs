using DSharpPlus.Commands;
using DSharpPlus.Commands.ContextChecks;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Entities;
using Voidway.ContextChecks;

namespace Voidway.Modules;

// ReSharper disable once StringLiteralTypo
[Command("threadowner")]
public class ThreadOwner(Bot bot) : ModuleBase(bot)
{
    // ReSharper disable once StringLiteralTypo
    [Command("deletemsg")]
    [SlashCommandTypes(DiscordApplicationCommandType.MessageContextMenu)]
    [RequireThreadOwner]
    [RequireGuild]
    public async Task DeleteMessage(SlashCommandContext ctx, DiscordMessage msg)
    {
        if (ctx.Guild is null)
        {
            await ctx.RespondAsync("This can only be run from a server!", true);
            return;
        }
        
        var cfg = ServerConfig.GetConfig(ctx.Guild.Id);
        
        if (!cfg.threadOwnersCanDeleteMessages)
        {
            await ctx.RespondAsync("This server doesn't let thread owners delete messages.", true);
            return;
        }

        bool success = await TryDeleteAsync(msg, $"Thread owner requested it");

        await ctx.RespondAsync(success ? "Done!" : "Failed... Maybe *I* don't have the perms to delete it?", true);
    }
    
    // ReSharper disable once StringLiteralTypo
    [Command("pin")]
    [SlashCommandTypes(DiscordApplicationCommandType.MessageContextMenu)]
    [RequireThreadOwner]
    [RequireGuild]
    public async Task PinMessage(SlashCommandContext ctx, DiscordMessage msg)
    {
        if (ctx.Guild is null)
        {
            await ctx.RespondAsync("This can only be run from a server!", true);
            return;
        }
        
        var cfg = ServerConfig.GetConfig(ctx.Guild.Id);
        
        if (!cfg.threadOwnersCanPinMessages)
        {
            await ctx.RespondAsync("This server doesn't let thread owners pin/unpin messages.", true);
            return;
        }

        try
        {
            await msg.PinAsync();
            await ctx.RespondAsync($"Pinned! If you check the pins, they should now show that message");
        }
        catch (Exception ex)
        {
            Logger.Warn($"Error while pinning message in thread {ctx.Channel} at the request of its owner {ctx.User} - {msg}", ex);
            await ctx.RespondAsync($"Failed to pin message: {ex.GetType().FullName} - {ex.Message}", true);
        }
    }
    
    [Command("unpin")]
    [SlashCommandTypes(DiscordApplicationCommandType.MessageContextMenu)]
    [RequireThreadOwner]
    [RequireGuild]
    public async Task UnpinMessage(SlashCommandContext ctx, DiscordMessage msg)
    {
        if (ctx.Guild is null)
        {
            await ctx.RespondAsync("This can only be run from a server!", true);
            return;
        }
        
        var cfg = ServerConfig.GetConfig(ctx.Guild.Id);

        if (!cfg.threadOwnersCanPinMessages)
        {
            await ctx.RespondAsync("This server doesn't let thread owners pin/unpin messages.", true);
            return;
        }

        try
        {
            await msg.UnpinAsync();
            await ctx.RespondAsync($"Unpinned! If you check the pins, they should now no longer show that message");
        }
        catch (Exception ex)
        {
            Logger.Warn($"Error while unpinning message in thread {ctx.Channel} at the request of its owner {ctx.User} - {msg}", ex);
            await ctx.RespondAsync($"Failed to unpin message: {ex.GetType().FullName} - {ex.Message}", true);
        }
    }
    
    // ReSharper disable once StringLiteralTypo
    [Command("remove")]
    [SlashCommandTypes(DiscordApplicationCommandType.UserContextMenu)]
    [RequireThreadOwner]
    [RequireGuild]
    public async Task RemoveUser(SlashCommandContext ctx, DiscordUser user)
    {
        if (ctx.Guild is null)
        {
            await ctx.RespondAsync("This can only be run from a server!", true);
            return;
        }
        
        if (ctx.Channel is not DiscordThreadChannel thread || thread.ThreadMetadata.IsArchived)
        {
            await ctx.RespondAsync("This can only be run from an active thread!", true);
            return;
        }

        if (user is not DiscordMember member)
        {
            await ctx.RespondAsync("Target user was not supplied as a server member!", true);
            return;
        }
        
        var cfg = ServerConfig.GetConfig(ctx.Guild.Id);
        
        if (!cfg.threadOwnersCanRemoveThreadMembers)
        {
            await ctx.RespondAsync("This server doesn't let thread owners delete messages.", true);
            return;
        }

        try
        {
            await thread.RemoveThreadMemberAsync(member);
            Logger.Put($"Removed {member} from thread {thread} at the request of thread owner {ctx.User}");
            await ctx.RespondAsync($"Successfully removed {member.Username} from your thread.", true);
        }
        catch(Exception ex)
        {
            Logger.Warn("Error while removing thread member", ex);
            await ctx.RespondAsync($"There was an error: {ex.GetType().FullName} - {ex.Message}", true);
        }

    }
}
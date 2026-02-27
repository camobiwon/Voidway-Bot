using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.Exceptions;

namespace Voidway.Modules;

public partial class ModuleBase
{
    [DebuggerStepThrough]
    protected async void TryDeleteDontCare(DiscordMessage msg, string? reason = null)
    {
        try
        {
            if (reason is not null)
                Logger.Put($"Deleting message {msg} for reason '{reason}'");
            await msg.DeleteAsync(reason);
        }
        catch (DiscordException ex)
        {
            //could possibly return it?
            Logger.Warn($"{GetType().Name} failed to delete message (DiscordException) {msg}", ex);
        }
        catch (Exception e)
        {
            Logger.Warn($"{GetType().Name} failed to delete message (Unknown exception) {msg}", e);
        }
    }
    
    [DebuggerStepThrough]
    protected async Task<bool> TryDeleteAsync(DiscordMessage msg, string? reason = null)
    {
        try
        {
            if (reason is not null)
                Logger.Put($"Deleting message {msg} for reason '{reason}'");
            await msg.DeleteAsync(reason);
            return true;
        }
        catch (DiscordException ex)
        {
            //could possibly return it?
            Logger.Warn($"{GetType().Name} failed to delete message {msg}\n\t{ex}");
            return false;
        }
    }

    [DebuggerStepThrough]
    protected async Task<bool> TryDeleteAsync(DiscordMessage msg, DiscordEmoji reaction, DiscordUser user, string? reason = null)
    {
        try
        {
            await msg.DeleteReactionAsync(reaction, user, reason);
            return true;
        }
        catch (DiscordException ex)
        {
            //could possibly return it?
            Logger.Warn($"{GetType().Name} failed to delete {reaction} reaction from {user} on message {msg}\n\t{ex}");
            return false;
        }
    }

    [DebuggerStepThrough]
    protected async Task<DiscordMessage?> TryFetchMessage(DiscordChannel channel, ulong id, bool skipCache = false)
    {
        try
        {
            return await channel.GetMessageAsync(id, skipCache);
        }
        catch
        {
            Logger.Put($"Ignore the above log, {channel} had no message with the ID {id}", LogType.Debug);
            return null;
        }
    }

    [SuppressMessage("ReSharper", "EmptyGeneralCatchClause")]
    protected DiscordUser? GetUser(DiscordEventArgs args)
    {
        dynamic dynargs = args;
        try
        {
            return dynargs.User;
        }
        catch { }

        try
        {
            return dynargs.Member;
        }
        catch { }


        try
        {
            return dynargs.Author;
        }
        catch { }

        try
        {
            return dynargs.UserAfter;
        }
        catch { }

        return null;
    }
    
    
    public async Task<DiscordMessage?> GetMessageFromLink(string link)
    {
        if (bot.DiscordClient is null)
        {
            return null;
        }
        
        if (!link.Contains("/channels/"))
        {
            Logger.Put("Invalid message link: " + link);
            return null;
        }

        ulong? targetChannelId = null;
        ulong? targetMessageId = null;

        string[] linkParts = link.Split("/channels/");
        if (linkParts.Length != 2)
            return null;
        // skip the first ID (the guild ID, or "@me" if a DM) because the Discord API /channels/ endpoint doesn't care 
        ulong?[] ids = linkParts[1]
            .Split('/')
            .Skip(1)
            .Select(str => ulong.TryParse(str, out ulong res) ? (ulong?)res : null)
            .ToArray();
        if (ids.Length >= 2)
        {
            targetChannelId = ids[0];
            targetMessageId = ids[1];
        }

        if (!targetMessageId.HasValue || !targetChannelId.HasValue)
            return null;

        DiscordChannel? channel;
        
        try
        {
            channel = await bot.DiscordClient.GetChannelAsync(targetChannelId.Value);
        }
        catch (Exception ex)
        {
            Logger.Warn("Caught exception while attempting to fetch channel for jump link " + link, ex);
            return null;
        }

        try
        {
            DiscordMessage msg = await channel.GetMessageAsync(targetMessageId.Value);
            return msg;
        }
        catch
        {
            return null;
        }
    }

    internal static async Task<bool> TryReact(DiscordMessage message, params DiscordEmoji[] emojis)
    {
        try
        {
            foreach (DiscordEmoji emoji in emojis)
            {
                await message.CreateReactionAsync(emoji);

                if (emojis.Length != 1) 
                    await Task.Delay(1000); // discord is *really* tight on reaction ratelimits
            }
            return true;
        }
        catch (Exception ex)
        {
            Logger.Warn("Exception while reacting to message", ex);
            return false;
        }
    }
}

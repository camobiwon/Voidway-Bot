using DSharpPlus.Commands;
using DSharpPlus.Commands.ContextChecks;
using DSharpPlus.Entities;

namespace Voidway.ContextChecks;

public class RequireThreadOwnerAttribute : ContextCheckAttribute
{
    
}


// ReSharper disable once ClassNeverInstantiated.Global
public class RequireThreadOwnerCheck : IContextCheck<RequireThreadOwnerAttribute>
{
    public ValueTask<string?> ExecuteCheckAsync(RequireThreadOwnerAttribute attribute, CommandContext context)
    {
        if (!context.Channel.IsThread)
            return ValueTask.FromResult<string?>("Must be called from a thread");
        
        if (context.Channel is not DiscordThreadChannel thread)
            return ValueTask.FromResult<string?>("D#+ did not provide a thread object for this thread!");
        
        if (context.User.Id != thread.CreatorId)
            return ValueTask.FromResult<string?>($"Must be the thread owner (caller {context.User.Id} != owner {thread.CreatorId})");
        
        return ValueTask.FromResult<string?>(null);
    }
}
using System.Diagnostics;
using DSharpPlus.Commands;
using DSharpPlus.Commands.ContextChecks;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Entities;

namespace Voidway.ContextChecks;

// ReSharper disable once ClassNeverInstantiated.Global
public class ExecuteOnceCheck : IContextCheck<UnconditionalCheckAttribute>
{
    private static HashSet<ulong> seenInteractionIds = [];
    
    public ValueTask<string?> ExecuteCheckAsync(UnconditionalCheckAttribute attribute, CommandContext context)
    {
        if (context is not SlashCommandContext sctx) 
            return ValueTask.FromResult<string?>(null);
        
        ulong id = sctx.Interaction.Id;
        if (seenInteractionIds.Add(id) || Debugger.IsAttached) 
            return ValueTask.FromResult<string?>(null);
        
        string str = $"Itx {id} has already been seen! Re-executing on it *WILL* cause a problem!";
        Logger.Warn(str);
        Logger.Warn(new StackTrace(1).ToString());
        return ValueTask.FromResult<string?>(str);
    }
}
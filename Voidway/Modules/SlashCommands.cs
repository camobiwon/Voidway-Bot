using DSharpPlus.Commands;
using DSharpPlus.Commands.ContextChecks;
using DSharpPlus.Commands.Processors.SlashCommands;

namespace Voidway.Modules;

[Command("mgmt")]
[RequireApplicationOwner]
public class SlashCommands(Bot bot) : ModuleBase(bot)
{
    [Command("reloadcfg")]
    [RequireApplicationOwner]
    public static async Task ReloadConfig(SlashCommandContext ctx)
    {
        await ctx.Interaction.RespondOrAppend("Reloading config and persistent data...");
        
        Config.ReadConfig();
        PersistentData.ReadPersistentData();
        
        await ctx.Interaction.RespondOrAppend("Done!");
    }
}
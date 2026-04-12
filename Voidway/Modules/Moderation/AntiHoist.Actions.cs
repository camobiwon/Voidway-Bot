using DSharpPlus.Commands;
using DSharpPlus.Commands.ContextChecks;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Commands.Trees.Metadata;
using DSharpPlus.Entities;
using System.ComponentModel;

namespace Voidway.Modules.Moderation;

[Command("hoist")]
[AllowedProcessors(typeof(SlashCommandProcessor))]
public partial class AntiHoist : ModuleBase
{
    [Command("set")]
    [RequireGuild]
    [RequirePermissions([], [DiscordPermission.ManageGuild])]
    public async Task EnableAntiHoistCommand(
        SlashCommandContext ctx,
        [Description("Enables/disables the anti-hoist module.")]
        bool enable)
    {
        if (ctx.Member is null || ctx.Guild is null)
        {
            await ctx.RespondAsync("Huh... I seem to be missing important information for this interaction... Try again later?", true);
            return;
        }

        ServerConfig cfg = ServerConfig.GetConfig(ctx.Guild.Id);
        cfg.antiHoistingEnabled = enable;

        if (enable)
            await ctx.RespondAsync("Enabled anti-hoisting.", true);
        else
            await ctx.RespondAsync("Disabled anti-hoisting.", true);

        ServerConfig.WriteConfigToFile(cfg);
    }
}
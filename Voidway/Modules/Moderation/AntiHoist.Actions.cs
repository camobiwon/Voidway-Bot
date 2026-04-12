using DSharpPlus.Commands;
using DSharpPlus.Commands.ContextChecks;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Commands.Trees.Metadata;
using DSharpPlus.Entities;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace Voidway.Modules.Moderation
{
    [Command("hoist")]
    [AllowedProcessors(typeof(SlashCommandProcessor))]
    public partial class AntiHoist : ModuleBase
    {
        [Command("scantime")]
        [RequireGuild]
        [RequirePermissions([], [DiscordPermission.ManageGuild])]
        public async Task SetChannelCommand(
        SlashCommandContext ctx,
        [Description("How long to wait until the next hoist scan.")]
        int minutes = 5)
        {
            if (minutes == 0)
                minutes = 5;

            if (ctx.Member is null || ctx.Guild is null)
            {
                await ctx.RespondAsync("Huh... I seem to be missing important information for this interaction... Try again later?", true);
                return;
            }

            var cfg = ServerConfig.GetConfig(ctx.Guild.Id);

            cfg.hoistScanMinutes = (uint)minutes;

            string plural = minutes != 1 ? "minutes" : "minute";

            await ctx.RespondAsync($"Hoist scanning will now happen every {minutes} {plural}.", true);

            ServerConfig.WriteConfigToFile(cfg);
        }
    }
}

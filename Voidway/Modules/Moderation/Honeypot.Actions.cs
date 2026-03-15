using DSharpPlus.Commands;
using DSharpPlus.Commands.ContextChecks;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Commands.Trees.Metadata;
using DSharpPlus.Entities;
using System.ComponentModel;
using System.Threading.Channels;

namespace Voidway.Modules.Moderation;

[Command("honeypot")]
[AllowedProcessors(typeof(SlashCommandProcessor))]
public partial class Honeypot : ModuleBase
{
    [Command("set")]
    [RequireGuild]
    [RequirePermissions([], [DiscordPermission.ManageGuild])]
    public async Task SetChannelCommand(
        SlashCommandContext ctx,
        [Description("The channel to use as a honeypot.")]
        DiscordChannel channel)
    {
        if (ctx.Member is null || ctx.Guild is null)
        {
            await ctx.RespondAsync("Huh... I seem to be missing important information for this interaction... Try again later?", true);
            return;
        }

        if (channel.Id == 0)
        {
            await ctx.RespondAsync("Discord for some reason didn't set the channel ID. Try again mayhaps?", true);
            return;
        }

        var cfg = ServerConfig.GetConfig(ctx.Guild.Id);

        cfg.honeypotChannel = channel.Id;

        ServerConfig.WriteConfigToFile(cfg);

        ModerationLogOptions options = new()
        {
            Title = "Set Honeypot Channel",
            UserResponsible = ctx.Member,
            Description = $"Honeypot channel set to {Formatter.Mention(channel)}",
            Color = DiscordColor.Yellow
        };

        await ctx.RespondAsync("Set honeypot channel.", true);
        await AuditLogForwarding.LogModerationAction(ctx.Guild, options);
    }

    [Command("unset")]
    [RequireGuild]
    [RequirePermissions([], [DiscordPermission.ManageGuild])]
    public async Task UnsetChannelCommand(
        SlashCommandContext ctx)
    {
        if (ctx.Member is null || ctx.Guild is null)
        {
            await ctx.RespondAsync("Huh... I seem to be missing important information for this interaction... Try again later?", true);
            return;
        }

        var cfg = ServerConfig.GetConfig(ctx.Guild.Id);

        if (cfg.honeypotChannel == 0)
        {
            await ctx.RespondAsync("No channel to unset.", true);
            return;
        }

        cfg.honeypotChannel = 0;
        ServerConfig.WriteConfigToFile(cfg);

        ModerationLogOptions options = new()
        {
            Title = "Unset Honeypot Channel",
            UserResponsible = ctx.Member,
            Description = $"Unset honeypot channel.",
            Color = DiscordColor.Yellow
        };

        await ctx.RespondAsync("Unset honeypot channel.", true);
        await AuditLogForwarding.LogModerationAction(ctx.Guild, options);
    }

    [Command("whitelist")]
    [RequireGuild]
    [RequirePermissions([], [DiscordPermission.ManageGuild])]
    public async Task AddRoleToWhitelistCommand(
        SlashCommandContext ctx,
        [Description("Role to add to the honeypot whitelist.")]
        DiscordRole role)
    {
        if (ctx.Member is null || ctx.Guild is null)
        {
            await ctx.RespondAsync("Huh... I seem to be missing important information for this interaction... Try again later?", true);
            return;
        }

        if (role is null)
        {
            await ctx.RespondAsync("No role set. Did you forget to add one?", true);
            return;
        }

        var cfg = ServerConfig.GetConfig(ctx.Guild.Id);

        List<ulong> list = cfg.honeypotRoleWhitelist.ToList();

        list.Add(role.Id);

        cfg.honeypotRoleWhitelist = list.ToArray();

        ServerConfig.WriteConfigToFile(cfg);

        ModerationLogOptions options = new()
        {
            Title = "Added Role To Honeypot Whitelist",
            UserResponsible = ctx.Member,
            Description = $"Role: {role.Name}",
            Color = DiscordColor.White
        };

        await ctx.RespondAsync("Added role to honeypot whitelist.", true);
        await AuditLogForwarding.LogModerationAction(ctx.Guild, options);
    }

    [Command("remove")]
    [RequireGuild]
    [RequirePermissions([], [DiscordPermission.ManageGuild])]
    public async Task RemoveRoleFromWhitelistCommand(
        SlashCommandContext ctx,
        [Description("Role to remove from the honeypot whitelist.")]
        DiscordRole role)
    {
        if (ctx.Member is null || ctx.Guild is null)
        {
            await ctx.RespondAsync("Huh... I seem to be missing important information for this interaction... Try again later?", true);
            return;
        }

        if (role is null)
        {
            await ctx.RespondAsync("No role defined. Did you forget to add one?", true);
            return;
        }

        var cfg = ServerConfig.GetConfig(ctx.Guild.Id);

        List<ulong> list = cfg.honeypotRoleWhitelist.ToList();

        list.Remove(role.Id);

        cfg.honeypotRoleWhitelist = list.ToArray();

        ServerConfig.WriteConfigToFile(cfg);



        ModerationLogOptions options = new()
        {
            Title = "Removed Role From Honeypot Whitelist",
            UserResponsible = ctx.Member,
            Description = $"Role: {role.Name}",
            Color = DiscordColor.White
        };

        await ctx.RespondAsync("Removed role from honeypot whitelist.", true);
        await AuditLogForwarding.LogModerationAction(ctx.Guild, options);
    }
}

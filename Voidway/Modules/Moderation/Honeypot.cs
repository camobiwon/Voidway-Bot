using DSharpPlus.Entities;
using DSharpPlus.Entities.AuditLogs;
using DSharpPlus.EventArgs;
using System;

namespace Voidway.Modules.Moderation;

// This module beams the fuck out of anyone silly enough to message in the honeypot channel.
public partial class Honeypot(Bot bot) : ModuleBase(bot)
{
    public static string GenerateKickUpdateMessage(uint kicks)
    {
        string[] adjectives =
        {
            "obliterated",
            "destroyed",
            "#boomed",
            "banned",
            "mogged"
        };

        Random rand = new Random();
        int index = rand.Next(0, adjectives.Length);

        return $"Number of people ***{adjectives[index]}***: {kicks}";
    }

    protected override async Task MessageCreated(DiscordClient client, MessageCreatedEventArgs args)
    {
        // An actual channel has to be set, otherwise it should do nothing.
        var cfg = ServerConfig.GetConfig(args.Guild.Id);

        if (args.Author is not DiscordMember target)
            return;

        if (cfg.honeypotChannel != args.Channel.Id)
            return;

        // Do not ban anybody above you.
        if (target.Hierarchy >= args.Guild.CurrentMember.Hierarchy)
            return;

        // Do not ban anybody with the "Manage Messages" permission
        if (args.Channel.PermissionsFor(target).HasPermission(DiscordPermission.ManageMessages))
            return;

        // Ignore anyone on the role whitelist.
        // Messages will be deleted instead.
        ulong[] whitelistedRoles = cfg.honeypotRoleWhitelist;

        foreach (var role in target.Roles)
        {
            foreach (var whitelistedRole in cfg.honeypotRoleWhitelist)
            {
                if (role.Id == whitelistedRole)
                {
                    await TryDeleteAsync(args.Message);
                    return;
                }
            }
        }

        AuditLogForwarding.IgnoreThese.PushBack(new AuditLogInfo(
                client.CurrentUser,
                DiscordAuditLogActionType.Ban,
                DateTime.Now
            ));

        // User had no whitelisted roles.
        // Get that ass banned (GTAB).
        var options = new ModerationLogOptions()
        {
            Title = $"User {(cfg.kickInsteadOfBan ? "Kicked" : "Banned")} (automatic)",
            Description = args.Message.ToString(),
            UserResponsible = client.CurrentUser,
            Target = args.Author,
            Reason = "Talked in the Honeypot channel.",
            Color = cfg.kickInsteadOfBan ? DiscordColor.Yellow : DiscordColor.Red
        };

        await AuditLogForwarding.LogModerationAction(args.Guild, options);
        
        // WARNING:
        // The code below will ban people when ran in production.
        // Use with caution in a test environment/server first.
        try
        {
            await args.Guild.BanMemberAsync(args.Author, TimeSpan.FromMinutes(15), options.Reason);
            
            if (cfg.kickInsteadOfBan)
            {
                AuditLogForwarding.IgnoreThese.PushBack(new AuditLogInfo(
                    client.CurrentUser,
                    DiscordAuditLogActionType.Unban,
                    DateTime.Now
                ));
                
                await args.Guild.UnbanMemberAsync(args.Author, "Kicked, not banned: " + options.Reason);
            }

            cfg.honeypotKicks++;

            // Message already exists, so just edit it
            if (cfg.honeypotTallyMessageID != 0)
            {
                DiscordMessage message = await args.Channel.GetMessageAsync(cfg.honeypotTallyMessageID);
                await message.ModifyAsync(GenerateKickUpdateMessage(cfg.honeypotKicks));
            }

            ServerConfig.WriteConfigToFile(cfg);
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to ban {args.Author} (initiated by {client.CurrentUser} for '{options.Reason}')! Details below", ex);
            ServerConfig.WriteConfigToFile(cfg);
            return;
        }
    }

    protected override async Task MessageDeleted(DiscordClient client, MessageDeletedEventArgs args)
    {
        // An actual channel has to be set, otherwise it should do nothing.
        var cfg = ServerConfig.GetConfig(args.Guild.Id);

        if (cfg.honeypotChannel != args.Channel.Id)
            return;

        if (cfg.honeypotTallyMessageID != args.Message.Id)
            return;

        cfg.honeypotTallyMessageID = 0;

        ServerConfig.WriteConfigToFile(cfg);
    }
}

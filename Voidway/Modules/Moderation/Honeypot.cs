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
        [
            "obliterated",
            "destroyed",
            "#boomed",
            "banned",
            "mogged"
        ];
        
        int index = Random.Shared.Next(0, adjectives.Length);

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

        // Do not ban anybody above the bot
        if (target.Hierarchy >= args.Guild.CurrentMember.Hierarchy)
            return;

        // Don't act on anybody with the "Manage Messages" permission
        if (args.Channel.PermissionsFor(target).HasPermission(DiscordPermission.ManageMessages))
            return;

        // Ignore anyone on the role whitelist.
        // Messages will be deleted instead.
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

        

        // User had no whitelisted roles.
        // Get that ass banned (GTAB).
        AuditLogForwarding.IgnoreThese.PushBack(new AuditLogInfo(
            client.CurrentUser,
            DiscordAuditLogActionType.Ban,
            DateTime.Now
        ));
        
        await AuditLogForwarding.LogModerationActionSlim(args.Guild,
            $"User {(cfg.kickInsteadOfBan ? "Kicked" : "Banned")} (via Honeypot)",
            args.Message.ToString(),
            $"User: {args.Author.Username} ({args.Author.Id})");
        
        // WARNING:
        // The code below will ban people when ran in production.
        // Use with caution in a test environment/server first.
        try
        {
            // Unconditionally ban so we can leverage messageDeleteDuration
            // Use kickInsteadOfBan to make it ACT like a kick by allowing
            // them to rejoin after being banned.
            await args.Guild.BanMemberAsync(args.Author, TimeSpan.FromMinutes(30), "Talked in the honeypot channel");
            
            if (cfg.kickInsteadOfBan)
            {
                AuditLogForwarding.IgnoreThese.PushBack(new AuditLogInfo(
                    client.CurrentUser,
                    DiscordAuditLogActionType.Unban,
                    DateTime.Now
                ));

                await args.Guild.UnbanMemberAsync(args.Author, "Kicked, not banned for getting honeypotted");
            }

            TrackHoneypot(args.Guild, args.Author);

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
            Logger.Error($"Failed to ban {args.Author} (done by {client.CurrentUser} for getting honeypotted)! Details below", ex);
            ServerConfig.WriteConfigToFile(cfg);
            return;
        }
    }

    private void TrackHoneypot(DiscordGuild guild, DiscordUser target)
    {
        if (!PersistentData.values.moderationActions.TryGetValue(guild.Id, out var guildActionCalendar))
        {
            guildActionCalendar = [];
            PersistentData.values.moderationActions.Add(guild.Id, guildActionCalendar);
        }

        var today = DateOnly.FromDateTime(DateTime.Now);
        if (!guildActionCalendar.TryGetValue(today, out var userActions))
        {
            userActions = [];
            guildActionCalendar[today] = userActions;
        }

        if (!userActions.TryGetValue(target.Id, out var actionList))
        {
            actionList = [];
            userActions[target.Id] = actionList;
        }

        actionList.Add("Honeypotted");
        PersistentData.WritePersistentData();
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

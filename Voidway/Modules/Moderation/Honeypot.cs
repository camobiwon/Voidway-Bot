using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

namespace Voidway.Modules.Moderation;

// This module beams the fuck out of anyone silly enough to message in the honeypot channel.
public partial class Honeypot(Bot bot) : ModuleBase(bot)
{
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
                DSharpPlus.Entities.AuditLogs.DiscordAuditLogActionType.Ban,
                DateTime.Now
            ));

        // User had no whitelisted roles.
        // Get that ass banned (GTAB).
        var options = new ModerationLogOptions()
        {
            Title = "User Banned (automatic)",
            UserResponsible = client.CurrentUser,
            Target = args.Author,
            Reason = "Talked in the Honeypot channel.",
            Color = DiscordColor.Red
        };

        await AuditLogForwarding.LogModerationAction(args.Guild, options);
        
        // WARNING:
        // The code below will ban people when ran in production.
        // Use with caution in a test environment/server first.
        try
        {
            await args.Guild.BanMemberAsync(args.Author, TimeSpan.FromMinutes(15), options.Reason);
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to ban {args.Author} (initiated by {client.CurrentUser} for '{options.Reason}')! Details below", ex);
            return;
        }
    }
}

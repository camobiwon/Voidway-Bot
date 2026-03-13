using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using System;
using System.Collections.Generic;
using System.Text;

namespace Voidway.Modules.Moderation
{
    // This module beams the fuck out of anyone silly enough to message in the honeypot channel.
    public sealed class Honeypot(Bot bot) : ModuleBase(bot)
    {
        protected override async Task MessageCreated(DiscordClient client, MessageCreatedEventArgs args)
        {
            // An actual channel has to be set, otherwise it should do nothing.
            var cfg = ServerConfig.GetConfig(args.Guild.Id);
            var target = await args.Guild.GetMemberAsync(args.Author.Id);

            if (cfg.honeypotChannel != args.Channel.Id)
                return;

            // Do not ban anybody above you.
            if (target.Hierarchy >= args.Guild.CurrentMember.Hierarchy)
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
                        await args.Message.DeleteAsync();
                        return;
                    }
                }
            }

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

#if DEBUG
            return;
#endif
            // WARNING:
            // The code below will ban people when ran in production.
            // Use with caution in a test environment first.
            await args.Guild.BanMemberAsync(args.Author.Id);
        }
    }
}

using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Voidway.Modules;

namespace Voidway.Modules.Moderation;

public partial class AntiHoist(Bot bot) : ModuleBase(bot)
{
    private const string HOIST_CHARS = @"()-+=_][\\|;',.<>/?!@#$%^&*";

    protected override async Task GuildMemberAdded(DiscordClient client, GuildMemberAddedEventArgs args)
    {
        ServerConfig cfg = ServerConfig.GetConfig(args.Guild.Id);

        if (!cfg.antiHoistingEnabled)
            return;

        DiscordMember member = args.Member;
        DiscordMember self = args.Guild.CurrentMember;

        await RenameHoisterAsync(self, member);
    }

    protected override async Task GuildMemberUpdated(DiscordClient client, GuildMemberUpdatedEventArgs args)
    {
        ServerConfig cfg = ServerConfig.GetConfig(args.Guild.Id);

        if (!cfg.antiHoistingEnabled)
            return;

        DiscordMember member = args.Member;
        DiscordMember self = args.Guild.CurrentMember;

        await RenameHoisterAsync(self, member);
    }

    private async Task RenameHoisterAsync(DiscordMember self, DiscordMember member)
    {
        if (!self.Permissions.HasPermission(DiscordPermission.ManageNicknames))
            return;

        if (member.Hierarchy >= self.Hierarchy)
            return;

        try
        {
            if (!IsNameHoistable(member.DisplayName))
                return;

            // slice characters off the start of the name until it's no longer seen as a hoist.
            string newNickname = member.DisplayName;
            while (IsNameHoistable(newNickname))
            {
                newNickname = newNickname[1..];
            }

            // failsafe to avoid giving someone a blank nickname. avoids error.
            if (string.IsNullOrWhiteSpace(newNickname))
                newNickname = "hoist";
            
            await member.ModifyAsync(edit => edit.Nickname = newNickname);
        }
        catch (Exception ex)
        {
            Logger.Error("Exception in the AntiHoist module! Details below", ex);
        }
    }

    private bool IsNameHoistable(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        return HOIST_CHARS.Contains(name[0], StringComparison.InvariantCultureIgnoreCase);
    }
}
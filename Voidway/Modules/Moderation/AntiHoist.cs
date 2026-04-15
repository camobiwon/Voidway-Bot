using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Voidway.Modules;

namespace Voidway.Modules.Moderation;

public partial class AntiHoist(Bot bot) : ModuleBase(bot)
{
    private readonly string HoistableCharacters = @"()-+=_][\\|;',.<>/?!@#$%^&*";

    protected override async Task GuildMemberAdded(DiscordClient client, GuildMemberAddedEventArgs args)
    {
        ServerConfig cfg = ServerConfig.GetConfig(args.Guild.Id);

        if (!cfg.antiHoistingEnabled)
            return;

        DiscordMember member = args.Member;
        DiscordMember self = await args.Guild.GetMemberAsync(client.CurrentUser.Id);

        await RenameHoisterAsync(self, member);
    }

    protected override async Task GuildMemberUpdated(DiscordClient client, GuildMemberUpdatedEventArgs args)
    {
        ServerConfig cfg = ServerConfig.GetConfig(args.Guild.Id);

        if (!cfg.antiHoistingEnabled)
            return;

        DiscordMember member = args.Member;
        DiscordMember self = await args.Guild.GetMemberAsync(client.CurrentUser.Id);

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

            await member.ModifyAsync((edit) => edit.Nickname = "hoist");
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

        for (int i = 0; i < HoistableCharacters.Length; i++)
        {
            if (name[0] == HoistableCharacters[i])
                return true;
        }

        return false;
    }
}
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Modio;

namespace Voidway.Modules.Moderation
{
    public partial class AntiHoist(Bot bot) : ModuleBase(bot)
    {
        protected override async Task GuildMemberAdded(DiscordClient client, GuildMemberAddedEventArgs args)
        {
            DiscordMember member = args.Member;
            DiscordMember self = await args.Guild.GetMemberAsync(client.CurrentUser.Id);

            await RenameHoisterAsync(self, member);
        }

        protected override async Task GuildMemberUpdated(DiscordClient client, GuildMemberUpdatedEventArgs args)
        {
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

            if (string.IsNullOrEmpty(member.Nickname) || member.Nickname[0] != '!')
                return;

            await member.ModifyAsync((edit) => edit.Nickname = "hoist");
        }
    }
}

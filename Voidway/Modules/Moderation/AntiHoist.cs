using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

namespace Voidway.Modules.Moderation
{
    public partial class AntiHoist(Bot bot) : ModuleBase(bot)
    {
        private const int ONE_SECOND = 1000;
        private const int ONE_MINUTE = ONE_SECOND * 60;

        protected override async Task InitOneShot(GuildDownloadCompletedEventArgs args)
        {
            await Task.Run(() => AntiHoistRoutine(args));
        }

        private async void AntiHoistRoutine(GuildDownloadCompletedEventArgs args)
        {
            try
            {
                while (true)
                {
                    foreach (var guildKvp in args.Guilds)
                    {
                        DiscordGuild guild = guildKvp.Value;
                        DiscordMember ourself = await guild.GetMemberAsync(bot.DiscordClient.CurrentUser.Id);

                        if (!ourself.Permissions.HasPermission(DiscordPermission.ManageNicknames))
                            continue;

                        ServerConfig cfg = ServerConfig.GetConfig(guild.Id);
                        await Task.Delay(ONE_MINUTE * (int)cfg.hoistScanMinutes);

                        await foreach (var member in guild.GetAllMembersAsync())
                        {
                            if (member.Hierarchy >= ourself.Hierarchy)
                                continue;

                            if (string.IsNullOrEmpty(member.Nickname) || member.Nickname[0] != '!')
                                continue;

                            await member.ModifyAsync((model) => model.Nickname = "hoist");
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Error($"Exception inside of the AntiHoist routine! Details below.", e);
            }
        }
    }
}

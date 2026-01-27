using DSharpPlus.EventArgs;

namespace Voidway.Modules;

internal class IgnoreBots(Bot bot) : ModuleBase(bot)
{
    protected override bool GlobalStopEventPropagation(DiscordEventArgs eventArgs)
    {
        var user = GetUser(eventArgs);
        return user is not null && user.IsBot;
    }
}
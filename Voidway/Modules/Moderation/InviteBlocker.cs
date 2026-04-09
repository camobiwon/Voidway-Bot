using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

namespace Voidway.Modules.Moderation;

public class InviteBlocker(Bot bot) : ModuleBase(bot)
{
    private static readonly TimeSpan PublicMessageTimeout = TimeSpan.FromMinutes(5);
    private static readonly Dictionary<DiscordChannel, DateTime> LastPublicMessageTimes = [];
    
    protected override async Task MessageCreated(DiscordClient client, MessageCreatedEventArgs args)
    {
        var cfg = ServerConfig.GetConfig(args.Guild.Id);
        if (!cfg.filterGameInvites || string.IsNullOrWhiteSpace(cfg.sendWhenSomeoneSendsGameInvites))
            return; // don't filter invites in this server or msg not configured

        if (cfg.dontFilterGameInvitesIn.Contains(args.Channel.Id))
            return; // don't filter invites in this channel
        
        if (args.Message.Activity?.Type is not DiscordMessageActivityType.Join
            and not DiscordMessageActivityType.JoinRequest)
            return; // not an invitation
        
        if (args.Author is not DiscordMember member)
            return;

        if (args.Channel.PermissionsFor(member).HasPermission(DiscordPermission.ManageMessages)
            || (args.Channel is DiscordThreadChannel thread && thread.CreatorId == member.Id))
            return; // if they're a mod/helper OR it's their thread 

        if (member.Roles.Any(r => cfg.exemptRolesFromInviteFilter.Contains(r.Id)))
            return; // user has exempted role

        TryDeleteDontCare(args.Message);
        
        bool messageSucceeded = await TryMessageMember(member);
        if (!messageSucceeded)
            await SendDontPostInvitesMessage(args.Channel);
    }

    private async Task SendDontPostInvitesMessage(DiscordChannel channel)
    {
        // AKA "if a msg was sent during this session AND it was more than <timeout> ago"
        if (LastPublicMessageTimes.TryGetValue(channel, out DateTime lastMessageTime)
            && lastMessageTime.Add(PublicMessageTimeout) < DateTime.Now)
        {
            return;
        }
        
        LastPublicMessageTimes[channel] = DateTime.Now;
        
        string formatStr = ServerConfig.GetConfig(channel.Guild.Id).sendWhenSomeoneSendsGameInvites;
        string content = string.Format(formatStr, channel.Guild.Name);
        var msg = await channel.SendMessageAsync(content);

        _ = Task.Delay(PublicMessageTimeout).ContinueWith(_ => TryDeleteDontCare(msg));
    }

    private static async Task<bool> TryMessageMember(DiscordMember member)
    {
        try
        {
            string formatStr = ServerConfig.GetConfig(member.Guild.Id).sendWhenSomeoneSendsGameInvites;
            string content = string.Format(formatStr, member.Guild.Name);
            await member.SendMessageAsync(content);
         
            Logger.Put($"Messaged {member} after they sent an invite in {member.Guild}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to message {member} after they sent an invite in {member.Guild}", ex);
            return false;
        }
    }
}
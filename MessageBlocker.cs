using DSharpPlus;
using DSharpPlus.Entities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Voidway_Bot
{
    // the goal here isn't to be too overbearing. so no full word blocking, because automod handles that
    // just delete shit like fusion or blmp invites
    internal static class MessageBlocker
    {
        static Dictionary<ulong, DateTime> lastResponseTimes = new();

        internal static void HandleMessages(DiscordClient client)
        {
            Logger.Put("Adding handler for message blocking", Logger.Reason.Trace);
            client.MessageCreated += (_, e) => { FilterMessage(e); return Task.CompletedTask; };
        }

        private static async void FilterMessage(DSharpPlus.EventArgs.MessageCreateEventArgs e)
        {
            if (e.Guild is null || e.Author is not DiscordMember member) return;
            if (e.Channel.PermissionsFor(member).HasPermission(Permissions.ManageMessages)) return;

            if (IsFiltered(e.Message))
            {
                string application = e.Message.Application is not null ? $"w/ application {e.Message.Application.Name} (ID={e.Message.Application.Id})" : "";
                string author = $"{e.Message.Author!.Username}#{e.Message.Author.Discriminator}";
                Logger.Put($"Deleting message '{Logger.EnsureShorterThan(e.Message.Content ?? "<No Content>", 50)}' by {author} {application}", Logger.Reason.Debug);

                await TryDelete(e.Message);
                
                bool messageSuccess = await TryMessage(member, e.Guild);

                if (messageSuccess) return;
                await SendAndDeleteInvites(e.Channel);
            }
        }

        static async Task TryDelete(DiscordMessage msg)
        {
            try
            {
                await msg.DeleteAsync();
            }
            catch (Exception ex)
            {
                string application = msg.Application is not null ? $"w/ application {msg.Application.Name} (ID={msg.Application.Id})" : "";
                string author = $"{msg.Author!.Username}#{msg.Author.Discriminator}";
                Logger.Error($"Exception while deleting message '{Logger.EnsureShorterThan(msg.Content ?? "<No Content>", 50)}' by {author} {application}", ex);
            }
        }

        static async Task<bool> TryMessage(DiscordMember author, DiscordGuild guild)
        {
            string authorStr = $"{author.Username}#{author.Discriminator} (ID={author.Id})";
            try
            {
                await author.SendMessageAsync($"Hey, I saw you posted an invite to a multiplayer game (probably BLMP, BWMP, or Fusion) in {guild.Name}. I'm sorry, but that isn't allowed, however you can find people to play with in these servers instead:\n{string.Join("\n", Config.GetFilterInvites())}");
                Logger.Put($"Sent message to {authorStr} in asking to not post invites in {guild.Name}.");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Exception while trying to DM {authorStr} from guild {guild.Name}", ex);
                return false;
            }
        }

        static async Task SendAndDeleteInvites(DiscordChannel channel)
        {
            if (lastResponseTimes.TryGetValue(channel.GuildId!.Value, out DateTime time))
            {
                // if it hasnt been over <timeout> seconds since last response
                if (time + TimeSpan.FromSeconds(Config.GetFilterResponseTimeout()) > DateTime.Now)
                    return;
            }
            
            lastResponseTimes[channel.GuildId!.Value] = DateTime.Now;

            string channelName = channel.Parent is DiscordThreadChannel ? $"{channel.Parent.Name}->{channel.Name}" : channel.Name;
            Logger.Put($"Sending and deleting invites (in {Config.GetFilterResponseTimeout()}s) in {channelName}", Logger.Reason.Debug);

            try
            {
                DiscordMessage msg = await channel.SendMessageAsync($"Do not send invites in this server. You can find people to play with in these servers instead:\n{string.Join("\n", Config.GetFilterInvites())}");
                Logger.Put($"Sent message to #{channel.Name} in {channel.Guild.Name} asking to not post invites.");
                DeleteLater(msg);
            }
            catch(Exception ex)
            {
                Logger.Error("Exception while send-and-deleting invites", ex);
            }
        }

        static async void DeleteLater(DiscordMessage msg)
        {
            await Task.Delay(Config.GetFilterResponseTimeout() * 1000);

            await TryDelete(msg);
        }

        static bool IsFiltered(DiscordMessage msg)
        {
            if (!Config.IsFilterMessageServer(msg.Channel!.GuildId!.Value) || msg.Activity is null) return false;

            bool filteredByActivity = msg.Activity?.Type == MessageActivityType.Join;

            return filteredByActivity && !Config.IsJoinMessageAllowedIn(msg.ChannelId);
        }
    }
}

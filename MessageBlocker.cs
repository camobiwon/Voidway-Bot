using DSharpPlus;
using DSharpPlus.Entities;
using System;
using System.Collections.Generic;
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
            client.MessageCreated += FilterMessage;
        }

        private static async Task FilterMessage(DiscordClient sender, DSharpPlus.EventArgs.MessageCreateEventArgs e)
        {
            if (e.Message.Channel.GuildId is null || e.Guild is null) return;
            if (e.Guild.Permissions is null) return;
            if (!e.Guild.Permissions.Value.HasPermission(Permissions.ManageMessages)) return;

            if (IsFiltered(e.Message))
            {
                string application = e.Message.Application is not null ? $"w/ application {e.Message.Application.Name} (ID={e.Message.Application.Id})" : "";
                string author = $"{e.Message.Author.Username}#{e.Message.Author.Discriminator}";
                Logger.Put($"Deleting message '{Logger.EnsureShorterThan(e.Message.Content, 50)}' by {author} {application}", Logger.Reason.Debug);

                await TryDelete(e.Message);

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
                string author = $"{msg.Author.Username}#{msg.Author.Discriminator}";
                Logger.Error($"Exception while deleting message '{Logger.EnsureShorterThan(msg.Content, 50)}' by {author} {application}", ex);
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
            else
            {
                lastResponseTimes[channel.GuildId!.Value] = DateTime.Now;
            }

            string channelName = channel.Parent is DiscordThreadChannel thread ? $"{channel.Parent.Name}->{channel.Name}" : channel.Name;
            Logger.Put($"Sending and deleting invites (in {Config.GetFilterResponseTimeout()}s) in {channelName}", Logger.Reason.Debug);

            try
            {
                DiscordMessage msg = await channel.SendMessageAsync($"Do not send invites in this server. You can find people to play with in these servers instead:\n{string.Join("\n", Config.GetFilterInvites())}");
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
            if (!Config.IsFilterMessageServer(msg.Channel.GuildId!.Value) || msg.Application is null) return false;

            bool filteredByActivity = msg.Activity?.Type == MessageActivityType.Join;

            return filteredByActivity && !Config.IsMessageAllowedChannel(msg.ChannelId);
        }
    }
}

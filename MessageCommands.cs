using DSharpPlus;
using DSharpPlus.Entities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Voidway_Bot
{
    internal static class MessageCommands // commands triggered by messages and not by slash commands
    {
        public static void HandleMessages(DiscordClient client)
        {
            Logger.Put("Adding handler for message commands", Logger.Reason.Trace);
            client.MessageCreated += (_, e) => { HandleMessage(e); return Task.CompletedTask; };
        }

        private static async void HandleMessage(DSharpPlus.EventArgs.MessageCreateEventArgs e)
        {
            if (e.Guild is null || e.Author is not DiscordMember member) return;
            if (Config.IsUserOwner(member.Id)) return;
            if (!e.MentionedUsers.Contains(Bot.CurrUser) || (e.Message.MessageType.HasValue && e.Message.MessageType.Value.HasFlag(MessageType.Reply))) return;

            string[] args = e.Message.Content.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).ToArray();

            if (args.Length == 0) return;

            switch (args[0])
            {
                case "pid":
                    try
                    {
                        Process proc = Process.GetCurrentProcess();
                        await e.Channel.SendMessageAsync($"PID: {proc.Id}");
                    }
                    catch { }

                    break;
                default:
                    break;
            }
        }
    }
}

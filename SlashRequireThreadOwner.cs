using DSharpPlus.Entities;
using DSharpPlus;
using DSharpPlus.SlashCommands;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Voidway_Bot
{
    internal class SlashRequireThreadOwner : SlashCheckBaseAttribute
    {
        public override Task<bool> ExecuteChecksAsync(InteractionContext ctx)
        {
            if (ctx.Channel is not DiscordThreadChannel thread)
                return Task.FromResult(false);
            DiscordUser? user = thread.Users.FirstOrDefault(tm => tm.Id == ctx.User.Id);

            if (user is null)
                return Task.FromResult(false);

            if (user.Id != thread.CreatorId)
                return Task.FromResult(false);

            return Task.FromResult(true);
        }
    }
}

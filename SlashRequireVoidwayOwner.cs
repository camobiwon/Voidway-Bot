using DSharpPlus.SlashCommands;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Voidway_Bot
{
    internal class SlashRequireVoidwayOwner : SlashCheckBaseAttribute
    {
        public override Task<bool> ExecuteChecksAsync(InteractionContext ctx)
        {
            ulong id = ctx.User.Id;
            bool ret = Config.IsUserOwner(id);
            return Task.FromResult(ret);
        }
    }
}

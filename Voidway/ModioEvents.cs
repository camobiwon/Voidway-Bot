using Modio;
using Modio.Filters;
using Modio.Models;

namespace Voidway;

public static class ModioEvents
{
    
    public static async Task Init(Client clint)
    {
        try
        {
            var games = await clint.Games.Search().ToList();
            var bonelabGame = games.First(g => g.NameId == "bonelab");
            var bonelab = clint.Games[bonelabGame.Id];
            var bonelabMods = bonelab.Mods;
        }
        FetchLoop(clint);
    }
    
    static async void FetchLoop(ModsClient mods, CommentsClient comments)
    {
        while (true)
        {
            await Task.Delay(60 * 1000);
            
            
            
            try
            {
                

            }
            catch (Exception ex)
            {
                Logger.Warn("Exception in Mod.IO fetch loop", ex);
            }
        }
        
    }
}
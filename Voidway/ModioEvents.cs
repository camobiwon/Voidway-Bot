using Modio;
using Modio.Filters;
using Modio.Models;

namespace Voidway;

public record ModioEventArgs(ModsClient ModsClient, ModEvent Event);

public static class ModioEvents
{
    public static ModsClient? BonelabClient { get; private set; }
    public static event Func<ModioEventArgs, Task>? OnEvent;
    
    public static async Task Init(Client clint)
    {
        try
        {
            var games = await clint.Games.Search().ToList();
            var bonelabGame = games.First(g => g.NameId == "bonelab");
            var bonelab = clint.Games[bonelabGame.Id];
            var bonelabMods = bonelab.Mods;
            BonelabClient = bonelabMods;
        }
        catch (Exception ex)
        {
            Logger.Error("Exception while initializing Mod.IO API clients! Mod.IO API access won't work during this session!", ex);
            return;
        }
        FetchLoop(BonelabClient);
    }
    
    // RESHARPER I FUCKING KNOW, I'M ALREADY CATCHING EVERYTHING
    // ReSharper disable once AsyncVoidMethod
    static async void FetchLoop(ModsClient mods)
    {
        long getEventsSince = DateTimeOffset.Now.ToUnixTimeSeconds();
        while (true)
        {
            await Task.Delay(60 * 1000);

            Filter filter = Filter.Custom("date_added", Operator.GreaterThan, getEventsSince.ToString());
            try
            {
                await foreach (var modEvent in mods.GetEvents(filter).ToEnumerable())
                {
                    if (getEventsSince < modEvent.DateAdded)
                        getEventsSince = modEvent.DateAdded;
                    ProcessEvent(modEvent);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("Exception in Mod.IO fetch loop", ex);
            }
        }
        // ReSharper disable once FunctionNeverReturns
    }

    private static void ProcessEvent(ModEvent modEvent)
    {
        if (OnEvent is null)
            return;
        
        var args = new ModioEventArgs(BonelabClient, modEvent);
        
        foreach (var subscriber in OnEvent.GetInvocationList())
        {
            var func = (Func<ModioEventArgs, Task>)subscriber;
            Task.Run(async () => await func(args));
        }
    }
}
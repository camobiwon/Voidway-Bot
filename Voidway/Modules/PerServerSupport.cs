namespace Voidway.Modules;


public class PerServerSupport(Bot bot) : ModuleBase(bot)
{
    private record RefetcherData(Func<Task> Refetcher, string NameTag);
    
    private static readonly Dictionary<Bot, List<RefetcherData>> RefetchersDict = [];
    
    public static void RegisterRefetcher(Bot bot, Func<Task> refetcherFunc, string nameTag)
    {
        if (!RefetchersDict.ContainsKey(bot))
            RefetchersDict[bot] = [];

        if (bot.DiscordClient is not null)
        {
            Logger.Warn($"Not adding refetcher '{nameTag}' to bot for {bot.DiscordClient.CurrentUser}\n\t" +
                        $"(The bot is already started, this is likely an auto-created instance from the D#+ cmd runner)");
        }
        
        RefetchersDict[bot].Add(new RefetcherData(refetcherFunc, nameTag));
    }

    protected override async Task FetchGuildResources()
    {
        if (!RefetchersDict.TryGetValue(bot, out var refetchers) || refetchers.Count == 0)
        {
            Logger.Put($"Not running any refetchers for {bot.DiscordClient?.CurrentUser.ToString() ?? "an uninitialized bot"}");
            return;
        }
        
        Logger.Put($"Now running {refetchers.Count} refetchers");
        foreach (var refetchData in refetchers)
        {
            Logger.Put($"Running refetcher '{refetchData.NameTag}'", LogType.Debug);
            await refetchData.Refetcher();
        }
    }
}
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Modio;
using Modio.Filters;
using Modio.Models;

namespace Voidway;

public record ModioEventArgs(ModsClient ModsClient, ModEvent Event);

public static partial class ModioHelper
{
    private static readonly Regex NameIdExtractor = NameIdExtractionRegex();
    public static GameClient? BonelabClient { get; private set; }
    
    public static ModsClient? ModsClient => BonelabClient?.Mods;
    
    private const string GAME_NAME_ID = "bonelab";
    public static event Func<ModioEventArgs, Task>? OnEvent;
    
    public static async Task Init(Client clint)
    {
        try
        {
            var games = await clint.Games.Search().ToList();
            var bonelabGame = games.First(g => g.NameId == GAME_NAME_ID);
            BonelabClient = clint.Games[bonelabGame.Id];
        }
        catch (Exception ex)
        {
            Logger.Error("Exception while initializing Mod.IO API clients! Mod.IO API access won't work during this session!", ex);
            return;
        }
        FetchLoop(ModsClient!);
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
        
        // this code path doesn't get hit if BonelabClient is null
        var args = new ModioEventArgs(ModsClient!, modEvent);
        
        foreach (var subscriber in OnEvent.GetInvocationList())
        {
            var func = (Func<ModioEventArgs, Task>)subscriber;
            Task.Run(async () => await func(args));
        }
    }

    public static bool TryParseUrl(string url, [MaybeNullWhen(false)] out string clientType, [MaybeNullWhen(false)] out string nameId)
    {
        url = url.Split('?')[0].Split('#')[0];
        clientType = null;
        nameId = null;
        
        var match = NameIdExtractor.Match(url);
        if (!match.Success)
            return false;
        if (match.Groups.Count != 5)
        {
            var groups = match.Groups.Cast<Group>().Select(g => g.Value);
            Logger.Warn($"Expected 5 groups, got {match.Groups.Count} from {url} -- {string.Join(", ", groups)}");
            return false;
        }

        if (string.IsNullOrEmpty(match.Groups[^1].Value))
        {
            clientType = match.Groups[1].Value;
            nameId = match.Groups[2].Value;
            
            Logger.Put($"Parsed object type {clientType}, and object name-id {nameId} from URL {url} " +
                       $"(not enough fields for a game-specific object)", LogType.Debug);
            return true;
        }
        else
        {
            string gameNameId = match.Groups[2].Value;
            
            clientType = match.Groups[3].Value;
            nameId = match.Groups[4].Value;
            
            Logger.Put($"Parsed game name-id {gameNameId}, object type {clientType}, and object name-id {nameId} from URL {url}", LogType.Debug);
            
            
            if (gameNameId == GAME_NAME_ID)
                return true;
            
            Logger.Warn($"Expected Game NameId {GAME_NAME_ID}, got {gameNameId} instead in URL {url}");
            return false;
        }
    }

    /// <summary>
    /// Retrieves a Mod.IO mod from its URL.
    /// </summary>
    /// <param name="mods">Any ModsClient that can be used lol it just checks bonelab</param>
    /// <param name="urlOrNameId">If it contains a slash it'll be treated as a URL. Doesn't have to start with https or end with a slash.</param>
    /// <returns>The mod data that was found, or <see langword="null"/> if none or not a mod.</returns>
    public static async Task<Mod?> GetFromUrlOrNameId(this ModsClient mods, string urlOrNameId)
    {
        string? nameId = urlOrNameId;
        if (!urlOrNameId.Contains('/'))
        {
            if (!TryParseUrl(urlOrNameId, out var clientType, out nameId))
                return null;

            if (clientType != "m")
            {
                Logger.Warn($"Expected client type 'm', got '{clientType}' instead in URL {urlOrNameId}");
                return null;
            }
        }
        
        try
        {
            var searchClient = mods.Search(ModFilter.NameId.Eq(nameId));
            var modData = await searchClient.First();
            return modData;
        }
        catch (Exception ex)
        {
            Logger.Warn("Exception while fetching mod data.", ex);
            return null;
        }
    }
    
    [GeneratedRegex(@"mod\.io\/(\S)\/([^\/\s]+)\/?(\S)?\/?([^\/\s]+)?", RegexOptions.Compiled)]
    private static partial Regex NameIdExtractionRegex();
}
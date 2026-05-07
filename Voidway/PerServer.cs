using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using DSharpPlus.Entities;
using Voidway.Modules;

namespace Voidway;

/// <summary>
/// <b>DO NOT MAKE THESE STATIC!!!</b> Because of DSharpPlus's injection of clients into every snowflake object,
/// these values are also tied to clients.<br/>
/// Ensures per-server values remain up-to-date without manual intervention. <br/>
/// Values will be invalidated and refetched whenever <see cref="ModuleBase.FetchGuildResources"/> is called. <br/>
/// </summary>
/// <typeparam name="TValue">Any value that depends on per-server values</typeparam>
/// <seealso cref="PerServerSupport"/>
/// <seealso cref="Bot.FindModule(DiscordGuild)"/>
public class PerServer<TValue> : IEnumerable<(ulong, TValue)>
{
    public TValue? this[DiscordGuild guild] => values.GetValueOrDefault(guild.Id);
    public TValue? this[ulong serverId] => values.GetValueOrDefault(serverId);
    
    public IEnumerable<TValue> Values => values.Values.Where(v => v is not null)!;
    
    public IEnumerator<(ulong, TValue)> GetEnumerator()
    {
        foreach (var kvp in values)
        {
            if (kvp.Value is null)
                continue;
            yield return (kvp.Key, kvp.Value);
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
    
    public bool TryGetValue(DiscordGuild guild, [MaybeNullWhen(false)] out TValue value)
        => values.TryGetValue(guild.Id, out value);
    public bool TryGetValue(ulong serverId, [MaybeNullWhen(false)] out TValue value)
    {
        return values.TryGetValue(serverId, out value);
    }
    
    
    
    private readonly Dictionary<ulong, TValue?> values = [];
    private readonly Dictionary<ulong, string> binders = [];

    private readonly Func<ServerConfig, Task<TValue?>> getter;
    private readonly Func<ServerConfig, string> binderRetriever;
    
    public PerServer(Bot bindTo, Func<ServerConfig, TValue?> getter, Func<ServerConfig, string> reloadWhenChanged)
    : this(bindTo, cfg => Task.FromResult(getter(cfg)), reloadWhenChanged)
    {
        // this body only exists because the compiler demands it
        // this overload just shims the getter to run synchronously and return a task, lol
    }
    
    public PerServer(Bot bindTo, Func<ServerConfig, Task<TValue?>> valueGetter, Func<ServerConfig, string> reloadWhenChanged)
    {
        this.getter = async cfg =>
        {
            
            var guild = bindTo.DiscordClient?.Guilds.GetValueOrDefault(cfg.Id);
            string guildTag = guild is not null ? $"Server '{guild.Name}' (ID {cfg.Id}) " : $"Server w/ ID {cfg.Id}";
            
            var result = default(TValue);
            try
            {
                result = await valueGetter(cfg);
            }
            catch (Exception ex)
            {
                Logger.Warn($"Exception when getting {typeof(TValue).FullName} for {guildTag}", ex);
            }
            
            if (result is null)
                Logger.Put($"Setting null value for {typeof(TValue).FullName} in {guildTag}", LogType.Debug);
            else
                Logger.Put($"Setting value for {typeof(TValue).FullName} in {guildTag} to {result}", LogType.Debug);

            return result;
        };
        this.binderRetriever = reloadWhenChanged;
        
        ServerConfig.ConfigChanged += ConfigChanged;
        PerServerSupport.RegisterRefetcher(bindTo, RefetchAll, typeof(TValue).FullName + "-perserver");
    }
    
    private async Task RefetchAll()
    {
        binders.Clear();
        foreach (var serverId in values.Keys)
        {
            var cfg = ServerConfig.GetConfig(serverId);
            try
            {
                values[cfg.Id] = await getter(cfg);
                binders[cfg.Id] = binderRetriever(cfg);
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to refetch a {typeof(TValue).FullName} for a server with the ID {cfg.Id}", ex);
            }
        }
    }

    private async Task ConfigChanged(ServerConfig cfg)
    {
        string currBinder = binders.GetValueOrDefault(cfg.Id) ?? "";
        string newBinder = binderRetriever(cfg);
        if (newBinder == currBinder)
        {
            return;
        }

        Logger.Put($"Config changed, creating a new {typeof(TValue).FullName} -- binder went from '{currBinder}' to '{newBinder}'", LogType.Trace);
        values[cfg.Id] = await getter(cfg);
        binders[cfg.Id] = newBinder;
    }
}
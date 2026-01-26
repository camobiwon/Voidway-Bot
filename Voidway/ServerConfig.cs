using Microsoft.Extensions.Logging;
using Tomlet;
using Tomlet.Attributes;

namespace Voidway;

public class ServerConfig
{
    private const string CFG_PATH_FORMAT = "./servers/config-{0}.toml";
    private static Dictionary<ulong, ServerConfig> loadedConfigs = new();
    
    [TomlNonSerialized]
    public ulong Id { get; private set; }

    public ulong modLogChannel = 0;
    public ulong msgLogChannel = 0;
    public ulong allModsChannel
    
    
    public static ServerConfig? GetConfig(ulong id)
    {
        var cfg = loadedConfigs.GetValueOrDefault(id) ?? LoadConfigFromFile(id);

        if (cfg is null)
        {
            cfg = new();
            cfg.Id = id;
        }
        
        return cfg;
    }

    // Returns null if no file or file content is malformed
    private static ServerConfig? LoadConfigFromFile(ulong id)
    {
        string path = string.Format(CFG_PATH_FORMAT, id.ToString());
        if (!Path.Exists(path))
            return null;

        string fileContent = "<Failed to read file>";

        try
        {
            fileContent = File.ReadAllText(path);
            ServerConfig loadedCfg = TomletMain.To<ServerConfig>(fileContent);
          
            loadedCfg.Id = id;
            loadedConfigs[id] = loadedCfg;
            return loadedCfg;
        }
        catch (Exception e)
        {
            Logger.Warn($"Exception while loading per-server config file for server with ID '{id}'\nError-causing file content:\n{fileContent}", e);
        }

        return null;
    }
}
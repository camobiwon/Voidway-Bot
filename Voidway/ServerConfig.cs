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
    // not readonly because newly created configs have their files written and
    // their loadedAt times updated so IsOld doesnt erroneously return false
    [TomlNonSerialized]
    private DateTime loadedAt = DateTime.Now;

    [TomlPrecedingComment("Moderation section")]
    public ulong moderationLogChannel = 0;
    public ulong msgLogChannel = 0;
    public ulong memberModNotesChannel = 0;
    public ulong honeypotChannel = 0;
    public ulong[] honeypotRoleWhitelist = [];
    public bool kickInsteadOfBan = true;

    [TomlPrecedingComment("Mod.IO mod announcements")]
    public ulong allModsChannel = 0;
    public ulong avatarChannel = 0;
    public ulong levelChannel = 0;
    public ulong spawnableChanel = 0;
    public ulong utilityChanel = 0;
    public bool dontCensorModUploads = false;
    
    [TomlPrecedingComment("Stuff for Mod.IO moderators")]
    public ulong malformedUploadChannel = 0;
    public ulong commentModerationChannel = 0;
    
    [TomlPrecedingComment("Removes game invites from non-privileged users ")]
    public bool filterGameInvites = false;
    public ulong[] dontFilterGameInvitesIn = [];
    [TomlInlineComment("Use {0} to include the server's name.")]
    public string sendWhenSomeoneSendsGameInvites = "";
    public ulong[] exemptRolesFromInviteFilter = [];
    
    [TomlPrecedingComment("Thread owner controls")]
    public bool threadOwnersCanDeleteMessages;
    public bool threadOwnersCanRemoveThreadMembers;
    public bool threadOwnersCanPinMessages;
    public bool threadOwnersCanClearReactions;

    private bool IsOld()
    {
        string path = string.Format(CFG_PATH_FORMAT, Id.ToString());
        if (!Path.Exists(path))
            return false;
        FileInfo finf = new(path);

        return loadedAt < finf.LastWriteTime;
    }
    
    public static ServerConfig GetConfig(ulong id)
    {
        var cfg = loadedConfigs.GetValueOrDefault(id) ?? LoadConfigFromFile(id);

        if (cfg is not null) return cfg;
        
        cfg = new ServerConfig();
        cfg.Id = id;
        WriteConfigToFile(cfg);
        cfg.loadedAt = DateTime.Now; // so IsOld doesnt return "true"

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

    public static void WriteConfigToFile(ServerConfig? cfg)
    {
        if (cfg is null)
            return;
        
        string path = string.Format(CFG_PATH_FORMAT, cfg.Id.ToString());
        string? folder = Path.GetDirectoryName(path);
        if (folder is not null && !Directory.Exists(folder))
            Directory.CreateDirectory(folder);
        
        string tomlText = TomletMain.TomlStringFrom(cfg); 
        
        File.WriteAllText(path, tomlText);
    }
    
    
}
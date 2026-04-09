using System.Diagnostics.CodeAnalysis;
using Tomlet;
using Tomlet.Attributes;

namespace Voidway;

// ok so this actually has to be able to function in multiple servers
internal class Config
{
    public static event Action? ConfigChanged;
    public static Config values;
    private const string CFG_PATH = "./config.toml";
    
    public string logPath = "./logs/";
    public int maxLogFiles = 5;

    #region DSharpPlus/Discord API config
    
    public string discordToken = "";
    
    [TomlPrecedingComment("Warning, this shit WILL spam your logs. Only enable if you're experiencing startup issues.")]
    public bool logDiscordDebug = false;
    
    public string[] ignoreDiscordLogsWith = [ "unknown event" ];
    public string[] ignoreDiscordLogsFrom = [ "HttpClient" ];
    
    #endregion

    #region Mod.IO API config
    
    public string modioApiKey = "";
    [TomlPrecedingComment("Can be left blank if you only use an API key w/o OAuth2")]
    public string modioOAuth = "";

    #endregion

    #region OpenAI API config
    
    [TomlPrecedingComment("Must be filled in to use AI moderation endpoints")]
    public string openAiToken = "";

    #endregion

    #region Discord behavior
    
    [TomlPrecedingComment("Discord user IDs")]
    public ulong[] blockedUsers = [];

    #endregion

    #region Mod.IO behavior

    [TomlPrecedingComment("Checks mods' tags, names, and descriptions.")]
    public string[] dontAnnounceModsWith = [ "18+", "nsfw" ];
    [TomlPrecedingComment("Doesn't announce mods that have this many (or more) tags. Set to -1 (or a really high number) to disable.")]
    public int modioTagSpamThreshold = 15;
    [TomlPrecedingComment("If a mod is larger than this, in MB, then the bot won't download it to check for malformed uploads")]
    public int modioMaxFilesize = 512;

    #endregion
    

    static Config()
    {
        Console.WriteLine("Initializing config");
        if (!File.Exists(CFG_PATH))
        {
            File.WriteAllText(CFG_PATH, TomletMain.TomlStringFrom(new Config())); // mmm triple parenthesis, v nice
        }

        ReadConfig();
        WriteConfig();
    }

    public static void OutputRawTOML()
    {
        Console.WriteLine(File.ReadAllText(CFG_PATH));
    }

    [MemberNotNull(nameof(values))]
    public static void ReadConfig()
    {
        string configText = File.ReadAllText(CFG_PATH);
        values = TomletMain.To<Config>(configText);
    }

    public static void WriteConfig()
    {
        File.WriteAllText(CFG_PATH, TomletMain.TomlStringFrom(values));
        ConfigChanged?.Invoke();
    }
    
}
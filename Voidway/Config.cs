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
    
    internal string token = "";
    
    public string modioApiKey = "";
    [TomlPrecedingComment("Can be left blank if you only use an API key w/o OAuth2")]
    public string modioOAuth = "";

    [TomlPrecedingComment("Must be filled in to use AI moderation endpoints")]
    public string openAiToken = "";

    [TomlPrecedingComment("Discord users")]
    public ulong[] blockedUsers = [];

    [TomlPrecedingComment("Doesn't announce mods that have this many (or more) tags. Set to -1 (or a really high number) to disable.")]
    public int modioTagSpamThreshold = 15;

    [TomlPrecedingComment("If a mod is larger than this, in MB, then the bot won't download it to check for malformed uploads")]
    public int modioMaxFilesize = 512;
}
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
    
    public string modioToken = "";
    [TomlPrecedingComment("Can be left blank if you only use an API key w/o OAuth2")]
    public string modioOAuth = "";
}
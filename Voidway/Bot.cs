using Modio;
using OpenAI;
using Voidway.Modules;

namespace Voidway;

public class Bot
{
    public static readonly Dictionary<DiscordClient, Bot> Clients = new();
    
    // Mod.IO stuff
    public readonly Configured<Client?> ModIO = new(
        () =>
        {
            if (string.IsNullOrEmpty(Config.values.modioApiKey))
                return null;
            var options = new Client.Options(Config.values.modioApiKey);
            if (!string.IsNullOrEmpty(Config.values.modioOAuth))
                options.Token = Config.values.modioOAuth;

            return new Client(options);
        },
        () => Config.values.modioApiKey + Config.values.modioOAuth);
    
    // OpenAI stuff
    public readonly Configured<OpenAIClient?> OpenAi = new(
        () => string.IsNullOrEmpty(Config.values.openAiToken) ? null : new OpenAIClient(Config.values.openAiToken),
        () => Config.values.openAiToken);

    // Discord stuff
    private DiscordClientBuilder discordBuilder;
    public DiscordClient? DiscordClient { get; private set; }
    public DiscordClientBuilder DiscordBuilder => DiscordClient is not null ? throw new InvalidOperationException() : discordBuilder;

    
    
    public Bot(string discordToken)
    {
        DiscordIntents intents = DiscordIntents.AllUnprivileged | DiscordIntents.GuildMembers | DiscordIntents.MessageContents;
        discordBuilder = DiscordClientBuilder.CreateDefault(discordToken, intents);
        
        // probably the most jank/worst part of this but this wont have hundreds of modules so
        // its fine and doesnt need priority ordering

        InitializeModules();
    }

    private void InitializeModules()
    {
        new IgnoreBots
    }

    public async Task ConnectAsync()
    {
        
    }
}
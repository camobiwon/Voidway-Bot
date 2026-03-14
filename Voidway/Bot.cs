using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text;
using DSharpPlus.Commands;
using DSharpPlus.Commands.Exceptions;
using DSharpPlus.Commands.Processors.MessageCommands;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Commands.Processors.UserCommands;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.Interactivity;
using DSharpPlus.Interactivity.Enums;
using DSharpPlus.Interactivity.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Modio;
using OpenAI;
using Voidway.ContextChecks;
using Voidway.Modules;
using Voidway.Modules.Moderation;
using Voidway.Modules.Modio;

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
    private bool guildsDownloaded;
    
    
    public Bot(string discordToken)
    {
        DiscordIntents intents = DiscordIntents.AllUnprivileged | DiscordIntents.GuildMembers | DiscordIntents.MessageContents;
        discordBuilder = DiscordClientBuilder.CreateDefault(discordToken, intents);
        discordBuilder.ConfigureServices(x => x.AddSingleton(this));
        discordBuilder.DisableDefaultLogging();
        discordBuilder.ConfigureLogging(x => x.AddProvider(new DiscordLogger.Provider()));
        // discordBuilder.SetLogLevel(LogLevel.Trace);
        

        RelaunchParameters.SetupProcessStartMessage(Environment.GetCommandLineArgs(), discordBuilder);
        
        InitializeModules();
        SetupCommands();
    }

    static Type[] GetCommandTypes()
    {
        Type[] commandTypes = new Type[] {  }
            .Concat(ModuleBase.AllModules.Select(m => m.GetType())
                .Where(t => t.GetCustomAttribute<CommandAttribute>() is not null))
            .ToArray();
        Logger.Put($"Found {commandTypes.Length} command types!",LogType.Debug);
        foreach (var commandType in commandTypes)
        {
            Logger.Put($"Type {commandType.FullName} is a command type!",LogType.Debug);
        }
        return commandTypes;
    }
    
    private void SetupCommands()
    {
        if (ModuleBase.AllModules.Count == 0)
            throw new InvalidOperationException("Double check your init order!" +
                                                "You should have at least ONE module before trying to add their cmds!");

        discordBuilder.UseInteractivity(new InteractivityConfiguration()
        {
            ButtonBehavior = ButtonPaginationBehavior.Disable,
            PaginationBehaviour = PaginationBehaviour.WrapAround,
            Timeout = TimeSpan.FromMinutes(1),
        });
        discordBuilder.UseCommands((isp, ce) =>
        {
            ce.CommandErrored += CommandErrorHandler;
            ce.AddCheck<RequireThreadOwnerCheck>();
            ce.AddCommands(GetCommandTypes());
        }, new CommandsConfiguration()
        {
            UseDefaultCommandErrorHandler = false, // annoying fuck
        });
    }

    private async Task AddCommandsToAllGuilds(DiscordClient clint, GuildDownloadCompletedEventArgs args)
    {
        if (guildsDownloaded)
            return;
        guildsDownloaded = true;

        var commandsExt = clint.ServiceProvider.GetService<CommandsExtension>();
        var slashProcessor = commandsExt?.GetProcessor<SlashCommandProcessor>();
        if (commandsExt is null || slashProcessor is null)
            throw new NullReferenceException("Commands extension/command processor is null! Was UseCommands ever called?");

        var guildIds = args.Guilds.Keys.ToArray();
        Logger.Put($"Adding commands to all {guildIds.Length} guild(s) now...");
        var commands = GetCommandTypes();
        foreach (Type commandType in commands)
            commandsExt.AddCommand(commandType, guildIds);
        await commandsExt.RefreshAsync();
        await slashProcessor.RegisterSlashCommandsAsync(commandsExt);
        Logger.Put($"Done registering {commands.Length} commands!");
    }

    private async Task AddCommandsToNewGuild(DiscordClient clint, GuildCreatedEventArgs args)
    {
        if (!guildsDownloaded)
            return;
        
        var commandsExt = clint.ServiceProvider.GetService<CommandsExtension>();
        var slashProcessor = commandsExt?.GetProcessor<SlashCommandProcessor>();
        if (commandsExt is null || slashProcessor is null)
            throw new NullReferenceException("Commands extension/command processor is null! Was UseCommands ever called?");
        
        Logger.Put($"Adding commands to guild {args.Guild.Name}...");
        var commands = GetCommandTypes();
        commandsExt.AddCommands(commands, args.Guild.Id);
        await commandsExt.RefreshAsync();
        await commandsExt.RefreshAsync();
        await slashProcessor.RegisterSlashCommandsAsync(commandsExt);
        Logger.Put($"Done adding {commands.Length} commands to guild {args.Guild.Name}!");
    }

    [SuppressMessage("ReSharper", "ObjectCreationAsStatement")]
    [SuppressMessage("Performance", "CA1806:Do not ignore method results")]
    private void InitializeModules()
    {
        // probably the most jank/worst part of this but this wont have hundreds of modules,
        // so its fine and doesn't need priority ordering beyond "init blockers first"
        
        // blockers
        new IgnoreBots(this);
        new MessageRecorder(this);
        new InviteBlocker(this);
        
        // moderation modules
        new AuditLogForwarding(this);
        new ModerationTracker(this);
        new ModNotes(this);
        new VoidwayActions(this);
        new ThreadOwner(this);
        new BotManagement(this);
        new Honeypot(this);

        // modio modules
        new ModAnnouncements(this);
        new ModfileScanning(this);
        new CommentFlagging(this);
        
        foreach (var module in ModuleBase.AllModules)
        {
            module.ConfigureEventHandlers();
        }
    }

    public async Task ConnectAsync()
    {
        if (ModIO.Value is not null)
            await ModioHelper.Init(ModIO.Value);
        
        DiscordClient = discordBuilder.Build();
        Clients[DiscordClient] = this;
        Logger.Put("Connecting to Discord API!");
        await DiscordClient.ConnectAsync();
        Logger.Put("Fully connected to Discord!");
    }
    
    
    private async Task CommandErrorHandler(CommandsExtension sender, DSharpPlus.Commands.EventArgs.CommandErroredEventArgs args)
    {
        ChecksFailedException? checkEx = args.Exception as ChecksFailedException;
        string? checksFailedMsg = checkEx is null
            ? null
            : (checkEx.Errors.Count == 1
                ? "Failed check: "
                : "Failed checks:\n") + string.Join("\n", checkEx.Errors.Select(d => d.ErrorMessage));

        
        int randomNumber = Random.Shared.Next();
        Logger.Error($" [{randomNumber}] Exception while executing command on command object {args.CommandObject}", args.Exception);
        string userResponse = $"Exception while running your command! Tell the host/developer to look for {randomNumber} in the log!```\n{Logger.EnsureShorterThan(args.Exception.ToString(), 1750, "\n[cut off for Discord]")}```";

        if (checksFailedMsg is not null)
        {
            userResponse = checksFailedMsg;
        }

        if (args.Context is SlashCommandContext sctx)
        {
            
            if (checkEx is not null)
            {
                await sctx.Interaction.CreateResponseAsync(DiscordInteractionResponseType.ChannelMessageWithSource, new DiscordInteractionResponseBuilder().AsEphemeral().WithContent(checksFailedMsg!));
                return;
            }
            
            switch (sctx.Interaction.ResponseState)
            {
                case DiscordInteractionResponseState.Unacknowledged:
                {
                    await sctx.Interaction.CreateResponseAsync(DiscordInteractionResponseType.ChannelMessageWithSource, new DiscordInteractionResponseBuilder().AsEphemeral().WithContent(userResponse));
                }
                    break;
                case DiscordInteractionResponseState.Replied:
                {
                    await sctx.Interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder().WithContent(userResponse));
                }
                    break;
                case DiscordInteractionResponseState.Deferred:
                {
                    await sctx.Interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder().WithContent(userResponse));
                }
                    break;
            }
        }
        else
            await args.Context.RespondAsync(userResponse);
    }
}
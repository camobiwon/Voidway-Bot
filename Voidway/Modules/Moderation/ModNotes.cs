using DSharpPlus.Commands;
using DSharpPlus.Commands.ContextChecks;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.Exceptions;

namespace Voidway.Modules.Moderation;

[Command("modnotes")]
public class ModNotes(Bot bot) : ModuleBase(bot)
{
    private const string NOTES_FORMAT = "Notes for {0}:\n{1}";
    private const string SPLIT_ON = ":\n";
    private static readonly Dictionary<string, Func<ModalSubmittedEventArgs, Task>> ModalFollowups = [];


    private static async Task<DiscordChannel?> GetNotesChannel(DiscordMember member)
    {
        var cfg = ServerConfig.GetConfig(member.Guild.Id);
        if (cfg.memberModNotesChannel == default)
            return null;

        try
        {
            var channel = await member.Guild.GetChannelAsync(cfg.memberModNotesChannel);
            return channel;
        }
        catch
        {
            Logger.Warn($"Configured mod note channel {cfg.memberModNotesChannel} not found in {member.Guild}");
            return null;
        }
    }
    
    private static async Task<DiscordMessage?> GetNotesMessage(DiscordMember member)
    {
        if (!PersistentData.values.modNoteMessages.TryGetValue(member.Guild.Id, out var inGuildDict))
            return null;

        if (!inGuildDict.TryGetValue(member.Id, out var msgId))
            return null;
        
        var channel = await GetNotesChannel(member);
        if (channel is null)
            return null;
        
        try
        {
            var message = await channel.GetMessageAsync(msgId);
            return message;
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to get mod notes message (for {member}) from {channel} w/ msg ID {msgId}, see below for more details", ex);
            inGuildDict[member.Guild.Id] = msgId;
            return null;
        }
    }
        
    private const string VOIDWAY_NOTES_ID_START = "void.modnotes.";
    private const string VOIDWAY_NOTES_MODAL_ID_START = $"{VOIDWAY_NOTES_ID_START}check.";
    private const string VOIDWAY_MODAL_FIELD_START = $"{VOIDWAY_NOTES_ID_START}in.";
    private const string VOIDWAY_NOTES_FIELD_START =  $"{VOIDWAY_MODAL_FIELD_START}txt.";
    protected override async Task ModalSubmitted(DiscordClient client, ModalSubmittedEventArgs args)
        {
            // not from us, ignore
            if (!args.Id.StartsWith(VOIDWAY_NOTES_ID_START))
                return;
            
            // dispatch waiting interaction
            if (ModalFollowups.TryGetValue(args.Id, out var followup))
            {
                await followup(args);
                return;
            }
            
            Logger.Warn($"There was no handler set up for an interaction with the ID {args.Id} and the following value(s):\n\t'{string.Join(",\n\t'", args.Values)}'");
            
            var dirb = new DiscordInteractionResponseBuilder()
                .AsEphemeral()
                .WithContent("Uh, there doesn't seem to be anything set up to handle that modal. Check with the dev?");
            await args.Interaction.CreateResponseAsync(DiscordInteractionResponseType.ChannelMessageWithSource, dirb);
        }

    [Command("notes")]
    [RequireGuild]
    [RequirePermissions(DiscordPermission.ModerateMembers)]
    [SlashCommandTypes(DiscordApplicationCommandType.UserContextMenu)]
    // [AllowedProcessors(typeof(UserCommandProcessor))]
    public static async Task GetModNotes(SlashCommandContext ctx, DiscordMember targetMember)
    {
        if (ctx.Member is null || ctx.Guild is null)
        {
            await ctx.RespondAsync("Huh... I seem to be missing important information for this interaction... Try again later?", true);
            return;
        }
        
        ulong notesChannelId = ServerConfig.GetConfig(ctx.Guild.Id).memberModNotesChannel;
        if (notesChannelId == default)
        {
            await ctx.RespondAsync("This server doesn't have a mod notes channel set up", true);
            return;
        }
        
        var notesChannel = await GetNotesChannel(targetMember);
        if (notesChannel is null)
        {
            await ctx.RespondAsync($"Hmm... I couldn't find a channel w/ ID {notesChannelId} here... Is config wrong?", true);
            return;
        }
        
        var notesMessage = await GetNotesMessage(targetMember);
        string[]? splitContent = notesMessage?.Content.Split(SPLIT_ON);
        string? notesStr = null;
        if (splitContent is not null)
        {
            notesStr = splitContent.Length > 1
                ? string.Join(SPLIT_ON, splitContent[1..])
                : "";
        }
        
        Logger.Put($"Checking mod notes for {targetMember} at the request of {ctx.Member}, sending modal (itx id {ctx.Interaction.Id})...");

        string observedModerations =
            Logger.EnsureShorterThan(ModerationTracker.GetObservationStringFor(targetMember), 1969);
        var notesInput = new DiscordTextInputComponent(
            VOIDWAY_NOTES_FIELD_START + targetMember.Id,
            "What would other moderators need to know about this user?",
            style: DiscordTextInputStyle.Paragraph,
            max_length: 1900);
        notesInput.Value = notesStr;
        
        var modalBuilder = new DiscordModalBuilder()
            .WithCustomId(VOIDWAY_NOTES_MODAL_ID_START + ctx.Interaction.Id)
            .WithTitle($"Mod notes for {targetMember.DisplayName} ({targetMember.Username})")
            .AddTextDisplay($"Observed moderation info:\n{observedModerations}")
            .AddTextInput(notesInput, $"Notes {(notesMessage is null ? "(Will create new message)" : "(Will edit existing message)")}");

        ModalFollowups[modalBuilder.CustomId] = async (args) =>
        {
            var notesSubmission = (TextInputModalSubmission)args.Values[notesInput.CustomId];
            string userStr = $"{targetMember.DisplayName} ({targetMember.Username}, {targetMember.Id})";
            string newMessageContent = string.Format(NOTES_FORMAT, userStr, notesSubmission.Value);

            if (string.IsNullOrWhiteSpace(notesSubmission.Value) && notesMessage is null)
            {
                await args.Interaction.RespondOrAppend("Gotcha, I'll create a notes message another time.");
                return;
            }

            if (notesMessage is null)
            {
                try
                {
                    notesMessage = await notesChannel.SendMessageAsync(newMessageContent);
                    
                    if (!PersistentData.values.modNoteMessages.TryGetValue(ctx.Guild.Id, out var modNoteMessages))
                    {
                        modNoteMessages = [];
                        PersistentData.values.modNoteMessages[ctx.Guild.Id] = modNoteMessages;
                    }

                    modNoteMessages[targetMember.Id] = notesMessage.Id;
                    
                    await args.Interaction.RespondOrAppend($"Created a [notes message]({notesMessage.JumpLink}) for that user!");
                    
                    PersistentData.WritePersistentData();
                    return;
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Failed to create a notes message in {notesChannel} (invoker {ctx.User})!");
                    await args.Interaction.RespondOrAppend($"Failed to create a notes message -- {ex.GetType().FullName}: {ex.Message}");
                    return;
                }
            }

            try
            {
                await notesMessage.ModifyAsync(newMessageContent);
                await args.Interaction.RespondOrAppend($"Successfully edited notes message!");
            }
            catch (Exception ex)
            {
                Logger.Error($"Exception while modifying notes message for {targetMember}", ex);
                await args.Interaction.RespondOrAppend($"Failed to edit notes message -- {ex.GetType().FullName}: {ex.Message}");
            }
            
        };

        await ctx.RespondWithModalAsync(modalBuilder);
    }
}
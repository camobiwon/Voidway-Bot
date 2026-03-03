using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

namespace Voidway.Modules.Moderation;

public partial class AuditLogForwarding
{
    // IDs
    private const string MOD_LOG_ID_START = "void.modlog.";
    private const string BUTTON_DISMISS_ID_START = $"{MOD_LOG_ID_START}dismiss.";
    private const string BUTTON_SENDREASON_ID_START = $"{MOD_LOG_ID_START}sendreason.";
    private const string BUTTON_CRAFTREASON_ID_START = $"{MOD_LOG_ID_START}craftreason.";
    private const string MODAL_INPUTREASON_ID_START = $"{MOD_LOG_ID_START}inputreason.";
    private const string MODAL_FIELD_INPUTREASON_ID_START = $"{MOD_LOG_ID_START}inputreason.field.";
    
    private static readonly Dictionary<string, Func<ComponentInteractionCreatedEventArgs, Task>> ButtonFollowups = [];
    private static readonly List<DiscordMessage> CurrentlyHandlingInteractionsOn = [];

    private static Dictionary<string, Func<ModalSubmittedEventArgs, Task>> ModalFollowups = [];
    
    protected override async Task ComponentInteractionCreated(DiscordClient client, ComponentInteractionCreatedEventArgs args)
    {
        // not from us, ignore
        if (!args.Id.StartsWith(MOD_LOG_ID_START))
            return;
        
        // dispatch waiting interaction
        if (ButtonFollowups.TryGetValue(args.Id, out var followup))
        {
            await followup(args);
            return;
        }

        Logger.Warn($"There was no handler set up for an interaction with the ID {args.Id} on {args.Message}");
        var dirb = new DiscordInteractionResponseBuilder()
            .AsEphemeral()
            .WithContent("Uh, there doesn't seem to be anything set up to handle that button press. Check with the dev?\n" +
                         "Either way, I'm going to remove the buttons from that message to avoid this happening again.");
        await args.Interaction.CreateResponseAsync(DiscordInteractionResponseType.ChannelMessageWithSource, dirb);
        
        if (args.Interaction.Message is not null)
        {
            await StripButtons(args.Interaction.Message, "Minor error - no button handler registered, oops!");
        }
    }

    protected override async Task ModalSubmitted(DiscordClient client, ModalSubmittedEventArgs args)
    {
        // not from us, ignore
        if (!args.Id.StartsWith(MOD_LOG_ID_START))
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

    public static void AddMessageButtons(DiscordMessageBuilder dmb, DiscordGuild guild, DiscordUser sendTo, ulong id, string actioned, string? autoReason)
    {
        bool hasAutoReason = !string.IsNullOrWhiteSpace(autoReason);
        var buttonAutoReason = new DiscordButtonComponent(
            DiscordButtonStyle.Primary,
            BUTTON_SENDREASON_ID_START + id,
            hasAutoReason ? "Send warning (Audit log reason)" : "Send warning (None in log)");
        var buttonCraftReason = new DiscordButtonComponent(
            DiscordButtonStyle.Secondary, 
            BUTTON_CRAFTREASON_ID_START + id,
            "Send warning (Custom reason)"); 
        var buttonDismiss = new DiscordButtonComponent(
            DiscordButtonStyle.Danger, 
            BUTTON_DISMISS_ID_START + id,
            "Dismiss");

        if (!hasAutoReason)
            buttonAutoReason.Disable();
        else 
            ButtonFollowups[buttonAutoReason.CustomId] = args => MessageUserWithReason(args.Interaction, sendTo, actioned, autoReason ?? "<No reason provided>", args.Message);

        ButtonFollowups[buttonCraftReason.CustomId] = args => CraftReason(args, sendTo, actioned);
        ButtonFollowups[buttonDismiss.CustomId] = args => DismissButtonClicked(args, sendTo);
        
        
        dmb.AddActionRowComponent(buttonAutoReason, buttonCraftReason, buttonDismiss);
    }

    private static async Task CraftReason(ComponentInteractionCreatedEventArgs args, DiscordUser sendTo, string actioned)
    {
        var textField = new DiscordTextInputComponent(MODAL_FIELD_INPUTREASON_ID_START + args.Id)
        {
            Style = DiscordTextInputStyle.Paragraph,
            MaximumLength = 1000,
            Placeholder = $"What reason should be sent to {sendTo.Username} to tell them why they're {actioned}?",
            Required = true,
        };

        var modal = new DiscordModalBuilder()
            .AddTextInput(textField, "Enter a reason...")
            .WithCustomId(MODAL_INPUTREASON_ID_START + args.Message.Id);

        ModalFollowups[modal.CustomId] = async modalArgs =>
        {
            var reasonSubmission = (TextInputModalSubmission)modalArgs.Values[textField.CustomId];
            string reason = reasonSubmission.Value;
            await MessageUserWithReason(modalArgs.Interaction, sendTo, actioned, reason, args.Message);
        };

        await args.Interaction.CreateResponseAsync(DiscordInteractionResponseType.Modal, modal);
        await StripButtons(args.Message, "Messaged user");
    }

    private static async Task DismissButtonClicked(ComponentInteractionCreatedEventArgs args, DiscordUser? originalTarget)
    {
        if (!await StripButtons(args.Message, "Not messaged"))
        {
            var dirbFail = new DiscordInteractionResponseBuilder()
                .AsEphemeral()
                .WithContent($"Discord won't let me edit the message... Strange!");
            await args.Interaction.CreateResponseAsync(DiscordInteractionResponseType.ChannelMessageWithSource, dirbFail);
            return;
        }

        var dirb = new DiscordInteractionResponseBuilder()
            .AsEphemeral()
            .WithContent($"Got it! Won't send a message to {originalTarget?.Username ?? "that user"}.");
        await args.Interaction.CreateResponseAsync(DiscordInteractionResponseType.ChannelMessageWithSource, dirb);
    }

    public static async Task<bool> StripButtons(DiscordMessage message, string? addToFooter = null)
    {
        if ((message.Components?.Count ?? 0) == 0 && string.IsNullOrWhiteSpace(addToFooter))
            return true;
        
        var dmb = new DiscordMessageBuilder(message);
        dmb.ClearComponents();
        
        var embedBuilders = dmb.Embeds.Select(embed => new DiscordEmbedBuilder(embed)).ToArray();

        var targetEmbed = embedBuilders.Length switch
        {
            0 => null,
            1 => embedBuilders[0],
            // for anything more than 1, try to find the main embed
            _ => embedBuilders.FirstOrDefault(e => e.Footer?.Text?.StartsWith("User:") ?? false)
        };

        if (targetEmbed is not null)
        {
            var footer = targetEmbed.Footer;
            if (footer is not null)
                footer.Text += "\n" + addToFooter;
            else
                targetEmbed.WithFooter(addToFooter);

            dmb.ClearEmbeds().AddEmbed(targetEmbed);
        }
        
        try
        {
            await message.ModifyAsync(dmb);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to strip buttons from a message {message}", ex);
            return false;
        }
    }

    public static async Task MessageUserWithReason(DiscordInteraction interaction,
        DiscordUser sendTo,
        string actioned,
        string reason,
        DiscordMessage? auditLogMessage = null)
    {
        
            if (auditLogMessage is not null && CurrentlyHandlingInteractionsOn.Contains(auditLogMessage))
            {
                var holdOnBuilder = new DiscordInteractionResponseBuilder()
                    .WithContent("Currently responding to another button press, please wait!")
                    .AsEphemeral();
                await interaction.CreateResponseAsync(DiscordInteractionResponseType.ChannelMessageWithSource, holdOnBuilder);
                return;
            }
            
            if (auditLogMessage is not null)
                CurrentlyHandlingInteractionsOn.Add(auditLogMessage);
            
            await interaction.RespondOrAppend($"Creating DM channel with {sendTo.Username}...");

            DiscordChannel dmChannel;
            try
            {
                dmChannel = await sendTo.CreateDmChannelAsync();
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to send message to {sendTo} @ creating DM channel", ex);
                await interaction.RespondOrAppend($"Failed to create DM channel with {sendTo.Username}");
                if (auditLogMessage is not null)
                    CurrentlyHandlingInteractionsOn.Remove(auditLogMessage);
                return;
            }

            await interaction.RespondOrAppend($"Created DM channel with {sendTo.Username}\nSending message...");

            try
            {
                await dmChannel.SendMessageAsync(
                    $"The moderators in **{interaction.Guild}** have {actioned} you for the following reason:\n" +
                    $"\"{reason}\"\n" +
                    $"-# *This bot doesn't relay messages to the staff of that server. If you want clarification, message a moderator / admin in that server.*");
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to send message to moderated user {sendTo}", ex);
                await interaction.RespondOrAppend($"Failed to send the message! *Whah!*");
                if (auditLogMessage is not null)
                    CurrentlyHandlingInteractionsOn.Remove(auditLogMessage);
                return;
            }


            if (auditLogMessage is not null)
            {
                await interaction.RespondOrAppend($"Created DM channel with {sendTo.Username}\nSent message!\nCleaning up...");

                // remove buttons for further invocations
                if (!await StripButtons(auditLogMessage, "Messaged user"))
                {
                    await interaction.RespondOrAppend("Tripped at the finish line, failed to edit the log message!\n-# Well, the important part got done, lol.");
                    CurrentlyHandlingInteractionsOn.Remove(auditLogMessage);
                    return;
                }
            }
            
            await interaction.RespondOrAppend($"**Sent {sendTo.Username} ({Formatter.Mention(sendTo)}) a message letting them know why they were {actioned}.**" +
                                              $"\n-# *Reason given: {reason}*");
            if (auditLogMessage is not null)
                CurrentlyHandlingInteractionsOn.Remove(auditLogMessage);
            
    }
}
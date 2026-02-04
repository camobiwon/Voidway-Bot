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
            .WithContent("Uh, there doesn't seem to be anything set up to handle that button press. Check with the dev?");
        await args.Interaction.CreateResponseAsync(DiscordInteractionResponseType.ChannelMessageWithSource, dirb);
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
            ButtonFollowups[buttonAutoReason.CustomId] = args => MessageUserWithReason(args, args.Message, sendTo, actioned, autoReason ?? "<No reason provided>");

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
            await MessageUserWithReason(modalArgs, args.Message, sendTo, actioned, reason);
        };

        await args.Interaction.CreateResponseAsync(DiscordInteractionResponseType.Modal, modal);
    }

    private static async Task DismissButtonClicked(ComponentInteractionCreatedEventArgs args, DiscordUser? originalTarget)
    {
        if (await StripButtons(args.Message))
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

    public static async Task<bool> StripButtons(DiscordMessage message)
    {
        if ((message.Components?.Count ?? 0) == 0)
            return true;
        
        var dmb = new DiscordMessageBuilder(message);
        dmb.ClearComponents();
        
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

    private static async Task MessageUserWithReason(InteractionCreatedEventArgs args, DiscordMessage msg, DiscordUser sendTo,
        string actioned, string reason)
    {
        
            if (CurrentlyHandlingInteractionsOn.Contains(msg))
            {
                var holdOnBuilder = new DiscordInteractionResponseBuilder()
                    .WithContent("Currently responding to another button press, please wait!")
                    .AsEphemeral();
                await args.Interaction.CreateResponseAsync(DiscordInteractionResponseType.ChannelMessageWithSource, holdOnBuilder);
                return;
            }
            
            CurrentlyHandlingInteractionsOn.Add(msg);
            
            var dwb = new DiscordWebhookBuilder();
            var dirb = new DiscordInteractionResponseBuilder()
                .AsEphemeral(false)
                .WithContent($"Creating DM channel with {sendTo.Username}...");
            await args.Interaction.CreateResponseAsync(DiscordInteractionResponseType.ChannelMessageWithSource, dirb);

            DiscordChannel dmChannel;
            try
            {
                dmChannel = await sendTo.CreateDmChannelAsync();
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to send message to {sendTo} @ creating DM channel", ex);
                dwb.WithContent($"Failed to create DM channel with {sendTo.Username}");
                await args.Interaction.EditOriginalResponseAsync(dwb);
                CurrentlyHandlingInteractionsOn.Remove(msg);
                return;
            }

            dwb.WithContent($"Created DM channel with {sendTo.Username}\nSending message...");
            await args.Interaction.EditOriginalResponseAsync(dwb);

            try
            {
                await dmChannel.SendMessageAsync(
                    $"The moderators in **{args.Interaction.Guild}** have {actioned} you for the following reason:\n" +
                    $"\"{reason}\"\n" +
                    $"-# *This bot doesn't relay messages to the staff of that server. If you want clarification, message a moderator / admin in that server.*");
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to send message to moderated user {sendTo}", ex);
                dwb.WithContent(dwb.Content + "\nTripped at the finish line, failed to edit message!\n-# Well, the important part got done, lol.");
                await args.Interaction.EditOriginalResponseAsync(dwb);
                CurrentlyHandlingInteractionsOn.Remove(msg);
                return;
            }

            
            dwb.WithContent($"Created DM channel with {sendTo.Username}\nSent message!\nCleaning up...");
            await args.Interaction.EditOriginalResponseAsync(dwb);

            // remove buttons for further invocations
            var builder = new DiscordMessageBuilder(msg);
            builder.ClearComponents();
            try
            {
                await msg.ModifyAsync(builder);
            }
            catch (Exception ex)
            {
                Logger.Warn("Failed to modify message to remove action components... Bruh?", ex);
                dwb.WithContent(dwb.Content + "\nTripped at the finish line, failed to edit message!\n-# Well, the important part got done, lol.");
                await args.Interaction.EditOriginalResponseAsync(dwb);
                CurrentlyHandlingInteractionsOn.Remove(msg);
                return;
            }

            dwb.WithContent(
                $"-# *Sent {sendTo.Username} ({Formatter.Mention(sendTo)}) a message letting them know why they were {actioned}. Reason given: {reason}*");
            await args.Interaction.EditOriginalResponseAsync(dwb);
            CurrentlyHandlingInteractionsOn.Remove(msg);
            
    }
}
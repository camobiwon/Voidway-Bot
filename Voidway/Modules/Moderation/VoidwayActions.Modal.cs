using DSharpPlus.Commands;
using DSharpPlus.Commands.ContextChecks;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Commands.Trees.Metadata;
using DSharpPlus.Entities;
using DSharpPlus.Entities.AuditLogs;
using DSharpPlus.EventArgs;

namespace Voidway.Modules.Moderation;

public partial class VoidwayActions
{
    private const string VOIDWAY_ACTION_ID_START = "void.act.";
    private const string VOIDWAY_TIMEOUT_MODAL_ID_START = $"{VOIDWAY_ACTION_ID_START}mute.";
    private const string VOIDWAY_KICK_MODAL_ID_START = $"{VOIDWAY_ACTION_ID_START}kick.";
    private const string VOIDWAY_BAN_MODAL_ID_START = $"{VOIDWAY_ACTION_ID_START}ban.";
    
    private const string VOIDWAY_MODAL_FIELD_START = $"{VOIDWAY_ACTION_ID_START}in.";
    private const string VOIDWAY_AUDIT_LOG_REASON_FIELD_START = $"{VOIDWAY_MODAL_FIELD_START}logreason.";
    private const string VOIDWAY_USER_MESSAGE_REASON_FIELD_START = $"{VOIDWAY_MODAL_FIELD_START}sendreason.";

    private const string VOIDWAY_DURATION_FIELD_START = $"{VOIDWAY_MODAL_FIELD_START}dur.";
    
    
    private static readonly Dictionary<string, Func<ModalSubmittedEventArgs, Task>> ModalFollowups = [];
    
    protected override async Task ModalSubmitted(DiscordClient client, ModalSubmittedEventArgs args)
    {
        // not from us, ignore
        if (!args.Id.StartsWith(VOIDWAY_ACTION_ID_START))
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

    private static readonly Dictionary<string, (string label, TimeSpan duration)> DurationTimespans = new()
    {
        { "time.none", ("None",             TimeSpan.Zero          ) },
        { "time.1h",   ("1 Hour",           TimeSpan.FromHours(1)  ) },
        { "time.12h",  ("12 Hours",         TimeSpan.FromHours(12) ) },
        { "time.1d",   ("1 Day",            TimeSpan.FromDays(1)   ) },
        { "time.3d",   ("3 Days",           TimeSpan.FromDays(3)   ) },
        { "time.7d",   ("7 Days",           TimeSpan.FromDays(7)   ) },
    };

    private static readonly DiscordSelectComponentOption[] DurationOptions = DurationTimespans
        .OrderBy(kvp => kvp.Value.duration)
        .Select(kvp => new DiscordSelectComponentOption(kvp.Value.label, kvp.Key))
        .ToArray();

    [Command("ban member")]
    [RequireGuild]
    [RequirePermissions(DiscordPermission.BanMembers)]
    [SlashCommandTypes(DiscordApplicationCommandType.UserContextMenu)]
    public async Task BanMember(SlashCommandContext ctx, DiscordMember targetMember)
    {
        if (ctx.Member is null || ctx.Guild is null)
        {
            await ctx.RespondAsync("Huh... I seem to be missing important information for this interaction... Try again later?", true);
            return;
        }

        if (targetMember.Hierarchy >= ctx.Member.Hierarchy)
        {
            await ctx.RespondAsync("You can't ban this member!", true);
            return;
        }
        
        
        if (targetMember.Hierarchy >= ctx.Guild.CurrentMember.Hierarchy)
        {
            await ctx.RespondAsync("I can't ban this member!", true);
            return;
        }
        
        Logger.Put($"Banning {targetMember} at the request of {ctx.Member}, sending modal first (itx id {ctx.Interaction.Id})...");
        
        var loggedReasonInput = new DiscordTextInputComponent(
            VOIDWAY_AUDIT_LOG_REASON_FIELD_START + targetMember.Id,
            "Required, why this user is being banned...",
            style: DiscordTextInputStyle.Paragraph,
            max_length: 1024);
        var sentReasonInput = new DiscordTextInputComponent(
            VOIDWAY_USER_MESSAGE_REASON_FIELD_START + targetMember.Id,
            "Not reqd, defaults to the logged reason...",
            required: false,
            style: DiscordTextInputStyle.Paragraph,
            max_length: 1024);
        var deleteDurationMenu = new DiscordSelectComponent(
            VOIDWAY_DURATION_FIELD_START + targetMember.Id,
            "Delete messages?",
            DurationOptions);

        var modalBuilder = new DiscordModalBuilder()
            .WithCustomId(VOIDWAY_BAN_MODAL_ID_START + ctx.Interaction.Id)
            .WithTitle($"Ban {targetMember.Username}?")
            .AddTextInput(loggedReasonInput, "Reason (for audit log)")
            .AddTextInput(sentReasonInput, "Reason (sent to user)")
            .AddSelectMenu(deleteDurationMenu, "Delete message history");

        ModalFollowups[modalBuilder.CustomId] = async args =>
        {
            var logReasonSubmission = (TextInputModalSubmission)args.Values[loggedReasonInput.CustomId];
            var sendReasonSubmission = (TextInputModalSubmission)args.Values[sentReasonInput.CustomId];
            var durationSubmission = (SelectMenuModalSubmission)args.Values[deleteDurationMenu.CustomId];
            Logger.Put($"Ban modal submitted (target: {targetMember}, caller: {ctx.Member}, guild: {ctx.Guild}, orig cmd itx id {ctx.Interaction.Id})");
            Logger.Put($"Parameters:\n" +
                       $"\t- Audit log reason: {logReasonSubmission.Value}\n" +
                       $"\t- Send reason: {(string.IsNullOrWhiteSpace(sendReasonSubmission.Value) ? "<None given>" : sendReasonSubmission.Value)}\n" +
                       $"\t- Duration ID(s? should just be one): [{string.Join(", ", durationSubmission.Values)}]", 
                LogType.Normal, false);

            string loggedReason = logReasonSubmission.Value;
            string sendReason = string.IsNullOrWhiteSpace(sendReasonSubmission.Value)
                ? loggedReason
                : sendReasonSubmission.Value;
            string durationId = durationSubmission.Values.FirstOrDefault() ?? "";
            var removeHistoryDuration = TimeSpan.Zero;
            if (DurationTimespans.TryGetValue(durationId, out var tuple))
            {
                removeHistoryDuration = tuple.duration;
            }

            await AuditLogForwarding.MessageUserWithReason(args.Interaction, targetMember, "banned", sendReason);

            AuditLogForwarding.IgnoreThese.PushBack(new AuditLogInfo(
                ctx.Client.CurrentUser,
                DiscordAuditLogActionType.Ban,
                DateTime.Now
            ));

            try
            {
                await targetMember.BanAsync(removeHistoryDuration, $"By {ctx.User.Username}: {loggedReason}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to ban {targetMember} (initiated by {ctx.User} for '{loggedReason}')! Details below", ex);
                await args.Interaction.RespondOrAppend($"Failed to ban user! {ex.GetType().FullName} {ex.Message}");
                return;
            }
            
            await args.Interaction.RespondOrAppend($"Done! User banned!");
            
            string? description = sendReason == loggedReason
                ? null
                : $"Sent reason: {sendReason}";
            
            var extraField = ("Moderation info", ModerationTracker.GetObservationStringFor(ctx.Guild.Id, ctx.Member.Id));

            var options = new ModerationLogOptions()
            {
                Title = "User banned (via command)",
                UserResponsible = ctx.Member,
                Reason = loggedReason,
                Color = DiscordColor.Red,
                Description = description,
                ExtraField = extraField,
            };

            await AuditLogForwarding.LogModerationAction(ctx.Guild, options);
        };
        
        await ctx.RespondWithModalAsync(modalBuilder);
    }
    
    
    [Command("kick member")]
    [RequireGuild]
    [RequirePermissions(DiscordPermission.KickMembers)]
    [SlashCommandTypes(DiscordApplicationCommandType.UserContextMenu)]
    public async Task KickMember(SlashCommandContext ctx, DiscordMember targetMember)
    {
        if (ctx.Member is null || ctx.Guild is null)
        {
            await ctx.RespondAsync("Huh... I seem to be missing important information for this interaction... Try again later?", true);
            return;
        }

        if (targetMember.Hierarchy >= ctx.Member.Hierarchy)
        {
            await ctx.RespondAsync("You can't kick this member!", true);
            return;
        }
        
        
        if (targetMember.Hierarchy >= ctx.Guild.CurrentMember.Hierarchy)
        {
            await ctx.RespondAsync("I can't kick this member!", true);
            return;
        }
        
        Logger.Put($"Kicking {targetMember} at the request of {ctx.Member}, sending modal first (itx id {ctx.Interaction.Id})...");
        
        var loggedReasonInput = new DiscordTextInputComponent(
            VOIDWAY_AUDIT_LOG_REASON_FIELD_START + targetMember.Id,
            "Required, why this user is being kicked...",
            style: DiscordTextInputStyle.Paragraph,
            max_length: 1024);
        var sentReasonInput = new DiscordTextInputComponent(
            VOIDWAY_USER_MESSAGE_REASON_FIELD_START + targetMember.Id,
            "Not reqd, defaults to the logged reason...",
            required: false,
            style: DiscordTextInputStyle.Paragraph,
            max_length: 1024);

        var modalBuilder = new DiscordModalBuilder()
            .WithCustomId(VOIDWAY_KICK_MODAL_ID_START + ctx.Interaction.Id)
            .WithTitle($"Kick {targetMember.Username}?")
            .AddTextInput(loggedReasonInput, "Reason (for audit log)")
            .AddTextInput(sentReasonInput, "Reason (sent to user)");

        ModalFollowups[modalBuilder.CustomId] = async args =>
        {
            var logReasonSubmission = (TextInputModalSubmission)args.Values[loggedReasonInput.CustomId];
            var sendReasonSubmission = (TextInputModalSubmission)args.Values[sentReasonInput.CustomId];
            Logger.Put($"Kick modal submitted (target: {targetMember}, caller: {ctx.Member}, guild: {ctx.Guild}, orig cmd itx id {ctx.Interaction.Id})");
            Logger.Put($"Parameters:\n" +
                       $"\t- Audit log reason: {logReasonSubmission.Value}\n" +
                       $"\t- Send reason: {(string.IsNullOrWhiteSpace(sendReasonSubmission.Value) ? "<None given>" : sendReasonSubmission.Value)}\n",
                LogType.Normal, false);

            string loggedReason = logReasonSubmission.Value;
            string sendReason = string.IsNullOrWhiteSpace(sendReasonSubmission.Value)
                ? loggedReason
                : sendReasonSubmission.Value;

            await AuditLogForwarding.MessageUserWithReason(args.Interaction, targetMember, "kicked", sendReason);

            AuditLogForwarding.IgnoreThese.PushBack(new AuditLogInfo(
                ctx.Client.CurrentUser,
                DiscordAuditLogActionType.Kick,
                DateTime.Now
            ));

            try
            {
                await targetMember.RemoveAsync($"By {ctx.User.Username}: {loggedReason}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to kick {targetMember} (initiated by {ctx.User} for '{loggedReason}')! Details below", ex);
                
                await args.Interaction.RespondOrAppend($"Failed to kick user! {ex.GetType().FullName} {ex.Message}");
                
                return;
            }

            await args.Interaction.RespondOrAppend($"Done! User kicked!");
            
            string? description = sendReason == loggedReason
                ? null
                : $"Sent reason: {sendReason}";
            
            var extraField = ("Moderation info", ModerationTracker.GetObservationStringFor(ctx.Guild.Id, ctx.Member.Id));

            var options = new ModerationLogOptions()
            {
                Title = "User kicked (via command)",
                UserResponsible = ctx.Member,
                Reason = loggedReason,
                Color = DiscordColor.Yellow,
                Description = description,
                ExtraField = extraField,
            };

            await AuditLogForwarding.LogModerationAction(ctx.Guild, options);
        };
        
        await ctx.RespondWithModalAsync(modalBuilder);
    }
    
    
    [Command("mute member")]
    [RequireGuild]
    [RequirePermissions(DiscordPermission.ModerateMembers)]
    [SlashCommandTypes(DiscordApplicationCommandType.UserContextMenu)]
    public async Task MuteMember(SlashCommandContext ctx, DiscordMember targetMember)
    {
        if (ctx.Member is null || ctx.Guild is null)
        {
            await ctx.RespondAsync("Huh... I seem to be missing important information for this interaction... Try again later?", true);
            return;
        }

        if (targetMember.Hierarchy >= ctx.Member.Hierarchy)
        {
            await ctx.RespondAsync("You can't mute this member!", true);
            return;
        }
        
        if (targetMember.Hierarchy >= ctx.Guild.CurrentMember.Hierarchy)
        {
            await ctx.RespondAsync("I can't mute this member!", true);
            return;
        }
        
        Logger.Put($"Muting {targetMember} at the request of {ctx.Member}, sending modal first (itx id {ctx.Interaction.Id})...");
        
        var durationMenu = new DiscordSelectComponent(
            VOIDWAY_DURATION_FIELD_START + targetMember.Id,
            "For how long?",
            DurationOptions.Skip(1));
        var loggedReasonInput = new DiscordTextInputComponent(
            VOIDWAY_AUDIT_LOG_REASON_FIELD_START + targetMember.Id,
            "Required, why this user is being muted...",
            style: DiscordTextInputStyle.Paragraph,
            max_length: 1024);
        var sentReasonInput = new DiscordTextInputComponent(
            VOIDWAY_USER_MESSAGE_REASON_FIELD_START + targetMember.Id,
            "Not reqd, defaults to the logged reason...",
            required: false,
            style: DiscordTextInputStyle.Paragraph,
            max_length: 1024);

        var modalBuilder = new DiscordModalBuilder()
            .WithCustomId(VOIDWAY_TIMEOUT_MODAL_ID_START + ctx.Interaction.Id)
            .WithTitle($"Mute {targetMember.Username}?")
            .AddTextInput(loggedReasonInput, "Reason (for audit log)")
            .AddTextInput(sentReasonInput, "Reason (sent to user)");

        ModalFollowups[modalBuilder.CustomId] = async args =>
        {
            var logReasonSubmission = (TextInputModalSubmission)args.Values[loggedReasonInput.CustomId];
            var sendReasonSubmission = (TextInputModalSubmission)args.Values[sentReasonInput.CustomId];
            var durationSubmission = (SelectMenuModalSubmission)args.Values[durationMenu.CustomId];
            Logger.Put($"Mute modal submitted (target: {targetMember}, caller: {ctx.Member}, guild: {ctx.Guild}, orig cmd itx id {ctx.Interaction.Id})");
            Logger.Put($"Parameters:\n" +
                       $"\t- Audit log reason: {logReasonSubmission.Value}\n" +
                       $"\t- Send reason: {(string.IsNullOrWhiteSpace(sendReasonSubmission.Value) ? "<None given>" : sendReasonSubmission.Value)}\n" +
                       $"\t- Duration ID(s? should just be one): [{string.Join(", ", durationSubmission.Values)}]", 
                LogType.Normal, false);

            string loggedReason = logReasonSubmission.Value;
            string sendReason = string.IsNullOrWhiteSpace(sendReasonSubmission.Value)
                ? loggedReason
                : sendReasonSubmission.Value;
            string durationId = durationSubmission.Values.FirstOrDefault() ?? "";
            var muteDuration = TimeSpan.Zero;
            if (DurationTimespans.TryGetValue(durationId, out var tuple))
            {
                muteDuration = tuple.duration;
            }

            await AuditLogForwarding.MessageUserWithReason(args.Interaction, targetMember, "muted", sendReason);

            AuditLogForwarding.IgnoreThese.PushBack(new AuditLogInfo(
                ctx.Client.CurrentUser,
                DiscordAuditLogActionType.MemberUpdate,
                DateTime.Now
            ));

            try
            {
                await targetMember.TimeoutAsync(DateTime.Now + muteDuration, $"By {ctx.User.Username}: {loggedReason}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to mute {targetMember} (initiated by {ctx.User} for '{loggedReason}')! Details below", ex);
                await args.Interaction.RespondOrAppend($"Failed to mute user! {ex.GetType().FullName} {ex.Message}");
                return;
            }
            
            await args.Interaction.RespondOrAppend($"Done! User muted!");
            
            
            string? description = sendReason == loggedReason
                ? $"Ends in {Formatter.Timestamp(muteDuration, TimestampFormat.RelativeTime)}"
                : $"Ends in {Formatter.Timestamp(muteDuration, TimestampFormat.RelativeTime)}\nSent reason: {sendReason}";
            
            var extraField = ("Moderation info", ModerationTracker.GetObservationStringFor(ctx.Guild.Id, ctx.Member.Id));

            var options = new ModerationLogOptions()
            {
                Title = "User muted (via command)",
                UserResponsible = ctx.Member,
                Reason = loggedReason,
                Color = DiscordColor.Yellow,
                Description = description,
                ExtraField = extraField,
            };

            await AuditLogForwarding.LogModerationAction(ctx.Guild, options);
        };
        
        await ctx.RespondWithModalAsync(modalBuilder);
    }

}
using System.ComponentModel;
using DSharpPlus.Commands;
using DSharpPlus.Commands.ContextChecks;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Commands.Processors.SlashCommands.ArgumentModifiers;
using DSharpPlus.Commands.Trees;
using DSharpPlus.Commands.Trees.Metadata;
using DSharpPlus.Entities;
using DSharpPlus.Entities.AuditLogs;
using DSharpPlus.EventArgs;

namespace Voidway.Modules.Moderation;

[Command("vw")]
[AllowedProcessors(typeof(SlashCommandProcessor))]
public partial class VoidwayActions(Bot bot) : ModuleBase(bot)
{
    [Command("ban")]
    [RequireGuild]
    [RequirePermissions(DiscordPermission.BanMembers)]
    public async Task BanMemberCommand(
        SlashCommandContext ctx,
        DiscordMember member,
        [Description("Will be sent to audit log")]
        string logReason,
        [Description("Will be sent to user. Defaults to the audit log reason if not given.")]
        string? sendReason = null,
        [Description("Up to 7 days | No spaces; ex. 1d45m20s = 1 day, 45 min, 20 sec")]
        TimeSpan? deleteMessages = null)
    {
        if (ctx.Member is null || ctx.Guild is null)
        {
            await ctx.RespondAsync("Huh... I seem to be missing important information for this interaction... Try again later?", true);
            return;
        }

        if (member.Hierarchy >= ctx.Member.Hierarchy)
        {
            await ctx.RespondAsync("You can't ban this member!", true);
            return;
        }
        
        if (member.Hierarchy >= ctx.Guild.CurrentMember.Hierarchy)
        {
            await ctx.RespondAsync("I can't ban this member!", true);
            return;
        }

        if (string.IsNullOrWhiteSpace(sendReason))
            sendReason = logReason;

        Logger.Put($"Banning {member} at the request of {ctx.Member}...");

        await AuditLogForwarding.MessageUserWithReason(ctx.Interaction, member, "banned", sendReason);
        
        AuditLogForwarding.IgnoreThese.PushBack(new AuditLogInfo(
            ctx.Client.CurrentUser,
            DiscordAuditLogActionType.Ban,
            DateTime.Now
        ));
        
        try
        {
            await member.BanAsync(deleteMessages ?? default, $"By {ctx.User.Username}: {logReason}");
            await ctx.Interaction.RespondOrAppend($"Messaged & banned {member.Username}!");
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to ban {member} (initiated by {ctx.User} for '{logReason}')! Details below", ex);
            await ctx.Interaction.RespondOrAppend($"Failed to ban {member.Username} -- {ex.GetType().FullName}: {ex.Message}");
            return;
        }
        
        var extraField = ("Moderation info", ModerationTracker.GetObservationStringFor(ctx.Guild.Id, ctx.Member.Id));
        
        var options = new ModerationLogOptions()
        {
            Title = "User Banned (via command)",
            UserResponsible = ctx.Member,
            Target = member,
            Reason = logReason,
            Color = DiscordColor.Red,
            ExtraField = extraField,
        };

        await AuditLogForwarding.LogModerationAction(ctx.Guild, options);
    }
    
    [Command("kick")]
    [RequireGuild]
    [RequirePermissions(DiscordPermission.KickMembers)]
    public async Task KickMemberCommand(
        SlashCommandContext ctx,
        DiscordMember member,
        [Description("Will be sent to audit log")]
        string logReason,
        [Description("Will be sent to user. Defaults to the audit log reason if not given.")]
        string? sendReason = null)
    {
        if (ctx.Member is null || ctx.Guild is null)
        {
            await ctx.RespondAsync("Huh... I seem to be missing important information for this interaction... Try again later?", true);
            return;
        }

        if (member.Hierarchy >= ctx.Member.Hierarchy)
        {
            await ctx.RespondAsync("You can't kick this member!", true);
            return;
        }
        
        if (member.Hierarchy >= ctx.Guild.CurrentMember.Hierarchy)
        {
            await ctx.RespondAsync("I can't kick this member!", true);
            return;
        }

        if (string.IsNullOrWhiteSpace(sendReason))
            sendReason = logReason;

        Logger.Put($"Kicking {member} at the request of {ctx.Member}...");

        await AuditLogForwarding.MessageUserWithReason(ctx.Interaction, member, "kicked", sendReason);
        string? content = null;
        try
        {
            var msg = await ctx.Interaction.GetOriginalResponseAsync();
            content = msg.Content;
        }
        catch
        {
            // Whatever, it just rewrites the content instead of appending.
        }
        DiscordWebhookBuilder dwb = new(); // will need to use after 
        
        AuditLogForwarding.IgnoreThese.PushBack(new AuditLogInfo(
            ctx.Client.CurrentUser,
            DiscordAuditLogActionType.Kick,
            DateTime.Now
        ));
        
        try
        {
            await member.RemoveAsync($"By {ctx.User.Username}: {logReason}");
            await ctx.Interaction.RespondOrAppend($"Messaged & kicked {member.Username}!");
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to kick {member} (initiated by {ctx.User} for '{logReason}')! Details below", ex);
            await ctx.Interaction.RespondOrAppend($"Failed to kick {member.Username} -- {ex.GetType().FullName}: {ex.Message}");
            return;
        }
        
        var extraField = ("Moderation info", ModerationTracker.GetObservationStringFor(ctx.Guild.Id, ctx.Member.Id));
        
        var options = new ModerationLogOptions()
        {
            Title = "User Kicked (via command)",
            UserResponsible = ctx.Member,
            Target = member,
            Reason = logReason,
            Color = DiscordColor.Yellow,
            ExtraField = extraField,
        };

        await AuditLogForwarding.LogModerationAction(ctx.Guild, options);
    }
    
    [Command("mute")]
    [Description("If the member is already muted, it overwrites that mute..")]
    [RequireGuild]
    [RequirePermissions(DiscordPermission.BanMembers)]
    public async Task MuteMemberCommand(
        SlashCommandContext ctx,
        DiscordMember member,
        [Description("Will be sent to audit log")]
        string logReason,
        [Description("Up to 28d | No spaces; ex. 1d45m20s = 1 day, 45 min, 20 sec")]
        TimeSpan duration,
        [Description("Will be sent to user. Defaults to the audit log reason if not given.")]
        string? sendReason = null)
    {
        if (ctx.Member is null || ctx.Guild is null)
        {
            await ctx.RespondAsync("Huh... I seem to be missing important information for this interaction... Try again later?", true);
            return;
        }

        if (member.Hierarchy >= ctx.Member.Hierarchy)
        {
            await ctx.RespondAsync("You can't mute this member!", true);
            return;
        }
        
        if (member.Hierarchy >= ctx.Guild.CurrentMember.Hierarchy)
        {
            await ctx.RespondAsync("I can't mute this member!", true);
            return;
        }
        
        if (string.IsNullOrWhiteSpace(sendReason))
            sendReason = logReason;

        Logger.Put($"Muting {member} at the request of {ctx.Member}...");

        await AuditLogForwarding.MessageUserWithReason(ctx.Interaction, member, "muted", sendReason);
        
        AuditLogForwarding.IgnoreThese.PushBack(new AuditLogInfo(
            ctx.Client.CurrentUser,
            DiscordAuditLogActionType.MemberUpdate,
            DateTime.Now
        ));
        
        try
        {
            await member.TimeoutAsync(DateTime.Now.Add(duration), $"By {ctx.User.Username}: {logReason}");
            await ctx.Interaction.RespondOrAppend($"Messaged & muted {member.Username}, they'll be unmuted {Formatter.Timestamp(duration, TimestampFormat.RelativeTime)}!");
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to mute {member} (initiated by {ctx.User} for '{logReason}')! Details below", ex);
            await ctx.Interaction.RespondOrAppend($"Failed to mute {member.Username} -- {ex.GetType().FullName}: {ex.Message}");
            return;
        }
        
        string? description = sendReason == logReason
            ? $"Ends in {Formatter.Timestamp(duration, TimestampFormat.RelativeTime)}"
            : $"Ends in {Formatter.Timestamp(duration, TimestampFormat.RelativeTime)}\nSent reason: {sendReason}";
        
        var extraField = ("Moderation info", ModerationTracker.GetObservationStringFor(ctx.Guild.Id, ctx.Member.Id));
        
        var options = new ModerationLogOptions()
        {
            Title = "User Muted (via command)",
            UserResponsible = ctx.Member,
            Target = member,
            Reason = logReason,
            Color = DiscordColor.Yellow,
            Description = description,
            ExtraField = extraField,
        };

        await AuditLogForwarding.LogModerationAction(ctx.Guild, options);
    }
}
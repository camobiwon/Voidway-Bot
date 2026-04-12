using System.Runtime.CompilerServices;
using DSharpPlus.Entities;
using Modio.Models;

namespace Voidway;

public static class VoidwayExtensions
{
    public static string FootnotePreviousLines(string content)
    {
        string[] lines = content.Split(Environment.NewLine);
        var footnotedLines = lines.Select(str =>
        {
            str = str.Replace("-# ", "");
            if (string.IsNullOrWhiteSpace(str))
                return "";
            
            return $"-# {str}";
        });
        return string.Join(Environment.NewLine, footnotedLines);
    }

    /// <summary>
    /// Responds to an interaction OR appends to its existing response, if one exists.
    /// </summary>
    /// <param name="itx">The interaction in question:</param>
    /// <param name="newResponse">Will be prefixed with a newline if the interaction has been responded-to already.</param>
    /// <param name="ephemeralIfNew">Whether the new response will be tagged as ephemeral</param>
    /// <param name="existingTextEditor">Edits the existing text of the respnose, if it exists</param>
    /// <exception cref="ArgumentOutOfRangeException">If D#+ adds a new response state I blow my shit off #BUTTHATSJUSTME</exception>
    public static async Task RespondOrAppend(this DiscordInteraction itx, string newResponse,
        bool ephemeralIfNew = true, Func<string, string>? existingTextEditor = null)

    {
        switch (itx.ResponseState)
        {
            case DiscordInteractionResponseState.Unacknowledged:
            {
                DiscordInteractionResponseBuilder dirb = new();
                dirb.AsEphemeral(ephemeralIfNew)
                    .WithContent(newResponse);
                await itx.CreateResponseAsync(DiscordInteractionResponseType.ChannelMessageWithSource, dirb);
                break;
            }
            case DiscordInteractionResponseState.Deferred:
            {

            }
                break;
            case DiscordInteractionResponseState.Replied:
            {
                string currentContent = "";
                try
                {
                    var msg = await itx.GetOriginalResponseAsync();
                    currentContent = msg.Content;
                }
                catch
                {
                    // dnc just means it would overwrite
                }

                DiscordWebhookBuilder dwb = new();
                if (string.IsNullOrWhiteSpace(currentContent))
                    dwb.WithContent(newResponse);

                existingTextEditor ??= FootnotePreviousLines;
                currentContent = existingTextEditor(currentContent);

                dwb.WithContent(currentContent + "\n" + newResponse);

                await itx.EditOriginalResponseAsync(dwb);

                break;
            }
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    /// <summary>
    /// Produces a loggable string that represents the mod.
    /// Why this isn't a stock feature of Modio.NET is beyond me.
    /// </summary>
    /// <param name="mod">Any mod, or null.</param>
    /// <param name="includeAuthorInfo">Determines whether to include the author's logtag as well.</param>
    /// <returns>ModName (mod-name-id #ID 123456789)</returns>
    /// <seealso cref="LogTag(User?)"/>
    public static string LogTag(this Mod? mod, bool includeAuthorInfo = false)
    {
        if (mod is null)
        {
            return $"Unknown mod (Null!)";
        }

        if (includeAuthorInfo)
            return $"{mod.Name} ({mod.NameId} #ID {mod.Id}) {mod.SubmittedBy.LogTag()}";
        else 
            return $"{mod.Name} ({mod.NameId} #ID {mod.Id})";
    }

    
    /// <summary>
    /// Produces a loggable string that represents the mod.
    /// Why this isn't a stock feature of Modio.NET is beyond me.
    /// </summary>
    /// <param name="user">Any user, or null.</param>
    /// <returns>ModName (mod-name-id #ID 123456789)</returns>
    /// <seealso cref="LogTag(Mod,bool)"/>
    public static string LogTag(this User? user)
    {
        if (user is null)
        {
            return $"Unknown user (Null!)";
        }
        
        return $"{user.Username} ({user.NameId} #ID {user.Id})";
    }
}
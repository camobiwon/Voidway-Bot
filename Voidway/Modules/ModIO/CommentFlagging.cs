using System.Reflection;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Modio.Filters;
using Modio.Models;
using OpenAI.Moderations;

namespace Voidway.Modules.ModIO;

internal class CommentFlagging(Bot bot) : ModuleBase(bot)
{
    private readonly PerServer<DiscordChannel> Channels = new(bot, async cfg =>
    {
        if (cfg.commentModerationChannel == 0)
            return null;

        if (bot.DiscordClient is null)
            return null;

        return await bot.DiscordClient.GetChannelAsync(cfg.commentModerationChannel);
    }, cfg => cfg.commentModerationChannel.ToString());
    
    private static readonly PropertyInfo[] categoryInfos = typeof(ModerationResult)
        .GetProperties()
        .Where(pi => pi.PropertyType == typeof(ModerationCategory))
        .ToArray();

    protected override Task InitOneShot(GuildDownloadCompletedEventArgs args)
    {
        ModioHelper.OnEvent += OnModioEvent;

        return Task.CompletedTask;
    }

    private async Task OnModioEvent(ModioEventArgs arg)
    {
        if (arg.Event.EventType != ModEventType.MOD_COMMENT_ADDED)
            return;

        var aiModeration = bot.OpenAi.Value?.GetModerationClient("omni-moderation-latest");
        if (aiModeration is null)
            return;

        var parentModClient = arg.ModsClient[arg.Event.ModId];
        var commentFilter = ModEventFilter.DateAdded.Eq(arg.Event.DateAdded);
        var search = parentModClient.Comments.Search(commentFilter);
        
        var commentData = await search.First();
        var modData = await parentModClient.Get();

        if (commentData?.Content is null)
        {
            Logger.Warn($"Mod.IO's API is well made: A comment (#ID {commentData?.Id.ToString() ?? "<NO ID LOL>"}) has no content? LOL? How?");
            return;
        }
        
        var moderationResult = (await aiModeration.ClassifyTextAsync(commentData.Content)).Value;
        if (!moderationResult.Flagged)
        {
            Logger.Put($"Comment (#ID {commentData.Id}) wasn't flagged by OpenAI (Content: {Logger.EnsureShorterThan(commentData.Content, 50)}).");
            return;
        }
        
        var flaggedCategories = GetFlaggedCategories(moderationResult);
        var max = flaggedCategories.MaxBy(tuple => tuple.confidence);
        Logger.Put($"Comment (#ID {commentData.Id}) was flagged in {flaggedCategories.Count} categories (Content: {Logger.EnsureShorterThan(commentData.Content, 50)})");
        Logger.Put($"Flagged categories: {string.Join(", ", flaggedCategories.Select(tup => $"{tup.name}: {Math.Round(tup.confidence)}%"))})");
        if (max.confidence < 0.5)
        {
            Logger.Put($"OpenAI's highest confidence is {max.name} @ {Math.Round(max.confidence * 100, 2)}% so we ignore, lol.");
            return;
        }
        
        // ok actually start building the "this person got flagged" message
        var author = commentData.SubmittedBy;

        if (author is null)
        {
            Logger.Warn($"Mod.IO's API messed up! We got a FLAGGED comment (#ID {commentData.Id}, info above) but didn't get the AUTHOR that posted it!!!");
            return;
        }
        
        string? authorName = author.Username ?? author.NameId;
        if (!string.IsNullOrWhiteSpace(authorName) && !string.IsNullOrWhiteSpace(author.ProfileUrl?.ToString()))
            authorName = $"[{authorName}](https://mod.io/g/bonelab/u/{author.NameId}/info#comments)";
        authorName ??= $"<A user with the number ID {author.Id}>";
        
        string? modName = modData.Name ?? modData.NameId;
        if (!string.IsNullOrWhiteSpace(modName) && !string.IsNullOrWhiteSpace(modData.ProfileUrl?.ToString()))
            modName = $"[{modName}]({modData.ProfileUrl.ToString()}#{commentData!.Id})";
        modName ??= $"<A mod with the number ID {modData.Id}>";

        string commentContent = Formatter.Sanitize(commentData!.Content).ReplaceLineEndings();

        string maxCategoryStr = Stringify(flaggedCategories.OrderByDescending(tuple => tuple.confidence).First());
        string allCategoriesStr = string.Join(" | ", flaggedCategories.Select(Stringify));

        string messageStr = $"**Comment by {authorName} on {modName} flagged!**\n\n" +
                            $"\"{Logger.EnsureShorterThan(commentContent, 500, "... *(truncated)*")}\"\n\n" +
                            $"Highest flagged category: {maxCategoryStr}\n" +
                            $"-# All categories {allCategoriesStr}";
        var dmb = new DiscordMessageBuilder()
            .WithContent(messageStr)
            .WithAllowedMentions([])
            .SuppressEmbeds();
        
        int successCount = 0;
        foreach (var channel in Channels.Values)
        {
            try
            {
                await channel.SendMessageAsync(dmb);
                successCount++;
            }
            catch (Exception ex)
            {
                Logger.Warn($"Exception while sending a flagged Mod.IO comment to {channel}", ex);
            }
        }

        Logger.Put($"Sent a notification to {successCount} channels about a flagged Mod.IO comment on {modName}");
    }

    private static string Stringify((string, float) tuple)
    {
        return $"**{tuple.Item1}** (*{Math.Round(tuple.Item2 * 100, 2)}%*)";
    }
    
    private static List<(string name, float confidence)> GetFlaggedCategories(ModerationResult moderationResult)
    {
        List<(string, float)> ret = [];
        
        foreach (var categoryPropertyInfo in categoryInfos)
        {
            var category = (ModerationCategory)categoryPropertyInfo.GetValue(moderationResult)!;
            if (!category.Flagged)
                continue;

            ret.Add((categoryPropertyInfo.Name, category.Score));
        }

        return ret;
    }
}
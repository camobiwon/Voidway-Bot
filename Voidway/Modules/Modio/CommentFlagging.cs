using System.Reflection;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Modio.Filters;
using Modio.Models;
using OpenAI.Moderations;

namespace Voidway.Modules.Modio;

internal class CommentFlagging(Bot bot) : ModuleBase(bot)
{
    private static readonly List<DiscordChannel> Channels = [];
    
    private static readonly PropertyInfo[] categoryInfos = typeof(ModerationResult)
        .GetProperties()
        .Where(pi => pi.PropertyType == typeof(ModerationCategory))
        .ToArray();
    
    protected override async Task FetchGuildResources()
    {
        if (bot.DiscordClient is null)
            return;

        Channels.Clear();
        
        foreach (var guildKvp in bot.DiscordClient.Guilds)
        {
            var cfg = ServerConfig.GetConfig(guildKvp.Key);

            if (cfg.malformedUploadChannel == 0)
                continue;

            var channel = await guildKvp.Value.GetChannelAsync(cfg.commentModerationChannel);
            Channels.Add(channel);
        }
        
        Logger.Put($"Got {Channels.Count} channels to send Mod.IO comment auto-scan warnings to");
    }

    protected override Task InitOneShot(GuildDownloadCompletedEventArgs args)
    {
        ModioEvents.OnEvent += OnModioEvent;

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
            return;
        
        var moderationResult = (await aiModeration.ClassifyTextAsync(commentData.Content)).Value;
        if (!moderationResult.Flagged)
            return;
        
        var flaggedCategories = GetFlaggedCategories(moderationResult);
        if (flaggedCategories.All(tuple => tuple.confidence > 0.5))
        {
            var max = flaggedCategories.MaxBy(tuple => tuple.confidence);
            Logger.Put($"OpenAI says a comment is flagged but the highest confidence is {max.name} @ {Math.Round(max.confidence * 100, 2)}% so we ignore");
            return;
        }
        
        // ok actually start building the "this person got flagged" message
        var author = commentData?.SubmittedBy;

        if (author is null)
            return;
        
        string? authorName = author.Username ?? author.NameId;
        if (!string.IsNullOrWhiteSpace(authorName) && !string.IsNullOrWhiteSpace(author.ProfileUrl?.ToString()))
            authorName = $"[{authorName}](https://mod.io/g/bonelab/{author.NameId}/info#comments)";
        authorName ??= $"<A user with the number ID {author.Id}>";
        
        string? modName = modData.Name ?? modData.NameId;
        if (!string.IsNullOrWhiteSpace(modName) && !string.IsNullOrWhiteSpace(modData.ProfileUrl?.ToString()))
            modName = $"[{modName}]({modData.ProfileUrl.ToString()}#{commentData!.Id})";
        modName ??= $"<A mod with the number ID {modData.Id}>";

        string commentContent = Formatter.Sanitize(commentData!.Content).ReplaceLineEndings();

        string maxCategoryStr = Stringify(flaggedCategories.OrderByDescending(tuple => tuple.confidence).First());
        string allCategoriesStr = string.Join(", ", flaggedCategories.Select(Stringify));

        string messageStr = $"# Comment by {authorName} on {modName} flagged!\n" +
                            $"\"{Logger.EnsureShorterThan(commentContent, 500, "... *(truncated)*")}\"\n" +
                            $"Highest flagged category: {maxCategoryStr}\n" +
                            $"-# All categories {allCategoriesStr}";
        var dmb = new DiscordMessageBuilder()
            .WithContent(messageStr)
            .WithAllowedMentions([])
            .SuppressEmbeds();
        
        int successCount = 0;
        foreach (var channel in Channels)
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

        Logger.Put($"Sent a notification to {successCount}/{Channels.Count} channels about a flagged Mod.IO comment on {modName}");
    }

    private static string Stringify((string, float) tuple)
    {
        return $"**{tuple.Item1}** *({Math.Round(tuple.Item2 * 100, 2)}%)*";
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
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using DSharpPlus.Commands;
using DSharpPlus.Commands.ArgumentModifiers;
using DSharpPlus.Commands.ContextChecks;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Entities;
using DSharpPlus.Interactivity;
using DSharpPlus.Interactivity.Extensions;
using Newtonsoft.Json;

namespace Voidway.Modules.Modio;

[SuppressMessage("ReSharper", "FieldCanBeMadeReadOnly.Local")]
file class ReuploadCatalogOverride
{
    // mod barcode (from .hash filename) -> nameid
    public Dictionary<string, string> barcodesToOriginalUploaders = [];
    // In case someone tries obscuring where their mod is originally from by renaming the .hash file
    public Dictionary<string, string> hashesToOriginalBarcodes = [];
}


internal partial class ModfileScanning
{
    [RequireApplicationOwner]
    [Command("cataloguser"), Description("(NOT EPHEMERAL) Scans a user's mods' files for new barcodes & hashes")]
    public async Task CatalogFromUserCmd(SlashCommandContext ctx, string modUrlOrNameId)
    {
        if (ModioHelper.ModsClient is null)
        {
            await ctx.RespondAsync("The Mod.IO API clien't isn't initialized.\n" +
                                   "The operator of the bot needs to set an API key and/or OAuth2 token, and restart the bot."
                , true);
            return;
        }

        if (!ModioHelper.TryParseUrl(modUrlOrNameId, out var clientType, out var nameId))
        {
            await ctx.RespondAsync($"That's not a valid URL, at least not one that I would know.", true);
            return;
        }
        
        if (clientType == "u")
        {
            // can you tell im getting sick of this API?
            await ctx.RespondAsync(
                $"Hey I know you want to scan all mods from {nameId}, who is *cough* **a user** *cough*," +
                $" to get their hashes and barcodes added to the reupload detection database," +
                $"but because **mod.io's API is infernal dogshit**," +
                $"I'm gonna need you to give me a link to **a mod that they've uploaded instead**\n" +
                $"Make sense? No? Blame mod.io!", true);
            return;
        }
        
        var modData = await ModioHelper.ModsClient.GetFromUrlOrNameId(modUrlOrNameId);

        if ((modData?.SubmittedBy?.Id ?? 0) == 0)
        {
            await ctx.RespondAsync("Looks like mod.io's API didn't give me anything to work with.\n" +
                                   "Are you sure that's a real mod?", true);
            return;
        }
        
        await ctx.RespondAsync($"Found {modData!.SubmittedBy!.Username}, starting cataloging now...");

        const int UPDATE_RATE_SEC = 5;
        var dwb = new DiscordWebhookBuilder();
        DateTime lastUpdate = DateTime.Now;
        var catalogResults = await CatalogBarcodeAndHashesFromUser(modData.SubmittedBy.Id, (updateStr) =>
        {
            // update only once every 3s max, because scans can take a really long time
            if (lastUpdate.AddSeconds(UPDATE_RATE_SEC) > DateTime.Now)
                return;
            lastUpdate = DateTime.Now;
            try
            {
                dwb.WithContent(Logger.ShowLastLinesOf(updateStr, 1950) + "\n*Scan ongoing...*");
                ctx.Interaction.EditOriginalResponseAsync(dwb);
            }
            catch
            {
                // dnc
            }
        });


        if (lastUpdate.AddSeconds(UPDATE_RATE_SEC) > DateTime.Now)
            await Task.Delay(UPDATE_RATE_SEC * 1000);
        
        
        dwb.WithContent(Logger.ShowLastLinesOf(catalogResults, 2000));
        await ctx.Interaction.EditOriginalResponseAsync(dwb);
    }
    
    [RequireApplicationOwner]
    [Command("catalogmod"), Description("(NOT EPHEMERAL) Scans a mod's files for new barcodes & hashes")]
    public async Task CatalogFromModCmd(SlashCommandContext ctx, string modUrlOrNameId)
    {
        if (ModioHelper.ModsClient is null)
        {
            await ctx.RespondAsync("The Mod.IO API clien't isn't initialized.\n" +
                                   "The operator of the bot needs to set an API key and/or OAuth2 token, and restart the bot."
                                   , true);
            return;
        }

        var modData = await ModioHelper.ModsClient.GetFromUrlOrNameId(modUrlOrNameId);
        if (modData is null)
        {
            await ctx.RespondAsync("Nothing found. Mod might not exist or the URL might not be a mod's?", true);
            return;
        }

        await ctx.RespondAsync($"Found {modData.Name}, starting cataloging now...");

        const int UPDATE_RATE_SEC = 5;
        var dwb = new DiscordWebhookBuilder();
        DateTime lastUpdate = DateTime.Now;
        var catalogResults = await CatalogBarcodeAndHashesFromMod(modData.Id, (updateStr) =>
        {
            // update only once every 3s max, because scans can take a really long time
            if (lastUpdate.AddSeconds(UPDATE_RATE_SEC) > DateTime.Now)
                return;
            lastUpdate = DateTime.Now;
            try
            {
                dwb.WithContent(Logger.ShowLastLinesOf(updateStr, 1950) + "\n*Scan ongoing...*");
                ctx.Interaction.EditOriginalResponseAsync(dwb);
            }
            catch
            {
                // dnc
            }
        });


        if (lastUpdate.AddSeconds(UPDATE_RATE_SEC) > DateTime.Now)
            await Task.Delay(UPDATE_RATE_SEC * 1000);
        
        
        dwb.WithContent(Logger.ShowLastLinesOf(catalogResults.displayString, 2000));
        await ctx.Interaction.EditOriginalResponseAsync(dwb);
    }

    [RequireApplicationOwner]
    [Command("overridecatalog"), Description("(Ephemeral) Takes a JSON file with reupload detection overrides to set.")]
    public async Task SetHashAndBarcodes(
        SlashCommandContext ctx,
        [Description("Run with nothing to see the format/example")] DiscordAttachment? file = null
        )
    {
        ReuploadCatalogOverride? catalogOverride = null;
        
        bool lacksFile = !(file?.FileName ?? "").EndsWith(".json", StringComparison.InvariantCultureIgnoreCase)
                            || string.IsNullOrWhiteSpace(file?.Url);
        if (!lacksFile)
        {
            try
            {
                var str = await DownloadClient.GetStringAsync(file!.Url);
                catalogOverride = JsonConvert.DeserializeObject<ReuploadCatalogOverride>(str);
            }
            catch (Exception ex)
            {
                Logger.Warn($"Exception while downloading/desrializing reupload overrides from {ctx.User}", ex);
                await ctx.RespondAsync($"Looks like there was an error downloading/deserializing the file you gave me:```cs\n{ex}\n```");
                return;
            }
        }
        
        if (lacksFile || catalogOverride is null)
        {
            // ReSharper disable once UseObjectOrCollectionInitializer
            var overrideExample = new ReuploadCatalogOverride();
            overrideExample.barcodesToOriginalUploaders["a.mod.barcode"] = "a-modio-uploader";
            overrideExample.hashesToOriginalBarcodes["deadbeefbac0de"] = "a.mod.barcode";
            string exampleJson = JsonConvert.SerializeObject(overrideExample, Formatting.Indented);
            await ctx.RespondAsync($"Input a JSON file like the following: ```json\n{exampleJson}\n```", true);
            return;
        }
        
        int noChangeBarcodes = 0;
        int overwrittenBarcodes = 0;
        int noChangeHashes = 0;
        int overwrittenHashes = 0;
        foreach (var barcodeOverrides in catalogOverride.barcodesToOriginalUploaders)
        {
            if (PersistentData.values.barcodesToOriginalUploaders.TryGetValue(barcodeOverrides.Key,
                    out var currUploaderAssoc))
            {
                if (currUploaderAssoc != barcodeOverrides.Value)
                    overwrittenBarcodes++;
                else
                    noChangeBarcodes++;
            }
            PersistentData.values.barcodesToOriginalUploaders[barcodeOverrides.Key] = barcodeOverrides.Value;
        }
        
        foreach (var hashOverrides in catalogOverride.hashesToOriginalBarcodes)
        {
            if (PersistentData.values.hashesToOriginalBarcodes.TryGetValue(hashOverrides.Key,
                    out var currBarcodeAssoc))
            {
                if (currBarcodeAssoc != hashOverrides.Value)
                    overwrittenHashes++;
                else
                    noChangeHashes++;
            }
            PersistentData.values.hashesToOriginalBarcodes[hashOverrides.Key] = hashOverrides.Value;
        }
        
        PersistentData.WritePersistentData();

        await ctx.RespondAsync($"Done setting {catalogOverride.hashesToOriginalBarcodes.Count} hash -> barcode relationships\n" +
                               $"-# ({overwrittenBarcodes} overwrite(s), {noChangeBarcodes} didn't change).\n" +
                               $"and {catalogOverride.barcodesToOriginalUploaders.Count} barcode -> mod.io uploader relationships\n" +
                               $"-# ({overwrittenHashes} overwrite(s), {noChangeHashes} didn't change).", true);
    }

    [RequireApplicationOwner]
    [Command("overridebarcode")]
    [Description("(Ephemeral) Marks a barcode as owned by a modio name ID, overriding if needed.")]
    public async Task SetBarcode(
        SlashCommandContext ctx,
        [Description("As shown in catalog_>>a.mod.barcode<<.hash")]
        string palletBarcode,
        [Description("As shown in the modder's profile URL")]
        string nameId)
    {

        if (PersistentData.values.barcodesToOriginalUploaders.TryGetValue(palletBarcode, out var currUploaderAssoc))
        {
            if (currUploaderAssoc != nameId)
            {
                await ctx.RespondAsync($"Changed {palletBarcode} from being associated with {currUploaderAssoc} to being associated with {nameId}.", true);
            }
            else
            {
                await ctx.RespondAsync($"No change. {palletBarcode} is already associated with {currUploaderAssoc}", true);
            }
        }
        else
        {
            await ctx.RespondAsync($"Added association from {palletBarcode} to {nameId}, where none existed before.", true);
        }
        
        PersistentData.values.barcodesToOriginalUploaders[palletBarcode] = nameId;
        
        PersistentData.WritePersistentData();
    }
    
    
    [RequireApplicationOwner]
    [Command("overridehash")]
    [Description("(Ephemeral) Marks a hash as being built by a  by a modio name ID, overriding if needed.")]
    public async Task SetHash(
        SlashCommandContext ctx,
        [Description("The 32-character contents of catalog_a.mod.barcode.hash")]
        string hash,
        [Description("As shown in catalog_>>a.mod.barcode<<.hash")]
        string palletBarcode)
    {

        if (PersistentData.values.hashesToOriginalBarcodes.TryGetValue(hash, out var currBarcodeAssoc))
        {
            if (currBarcodeAssoc != palletBarcode)
            {
                await ctx.RespondAsync($"Changed {hash} from being associated with {currBarcodeAssoc} to being associated with {palletBarcode}.", true);
            }
            else
            {
                await ctx.RespondAsync($"No change. {currBarcodeAssoc} is already associated with {palletBarcode}", true);
            }
        }
        else
        {
            await ctx.RespondAsync($"Added association from {hash} to {palletBarcode}, where none existed before.", true);
        }
        
        PersistentData.values.hashesToOriginalBarcodes[hash] = palletBarcode;
        
        PersistentData.WritePersistentData();
    }


    [RequireApplicationOwner]
    [Command("gethashes")]
    [Description("(Ephemeral) Shows you the hash-to-barcode associations.")]
    public async Task GetHashes(SlashCommandContext ctx,
        [Description("Show hashes tied to this barcode")]
        string? palletBarcode = null,
        [Description("Show what barcode this hash is tied to")]
        string? hash = null)
    {
        bool hashNull = string.IsNullOrWhiteSpace(hash);
        bool palletNull = string.IsNullOrWhiteSpace(palletBarcode);

        if (!hashNull && !palletNull)
        {
            await ctx.RespondAsync("Hey man you can't search for *both* things at the same time, lol", true);
            return;
        }

        if (!hashNull)
        {
            if (PersistentData.values.hashesToOriginalBarcodes.TryGetValue(hash!, out var currBarcodeAssoc))
                await ctx.RespondAsync($"{hash} points to the pallet barcode '{currBarcodeAssoc}'", true);
            else
                await ctx.RespondAsync($"{hash} isn't associated with anything", true);
            return;
        }
        
        if (!palletNull)
        {
            if (PersistentData.values.barcodesToOriginalUploaders.TryGetValue(palletBarcode!, out var currUploaderAssoc))
                await ctx.RespondAsync($"{palletBarcode} points to the mod.io user with the NameID '{currUploaderAssoc}'", true);
            else
                await ctx.RespondAsync($"{palletBarcode} isn't associated with any mod.io uploader", true);
            return;
        }

        List<Page> pages = [];
        int pageNum = 1;
        // await ctx.RespondAsync($"Looking up every hash association isn't done yet. It'll be done soon, hopefully!", true);
        StringBuilder pageBuilder = new();
        pageBuilder.AppendLine($"Page {pageNum}");
        foreach (var kvp in PersistentData.values.hashesToOriginalBarcodes.OrderBy(kvp => kvp.Value))
        {
            var line = $" - `{kvp.Key}` -> {kvp.Value}`";
            if (pageBuilder.Length + line.Length > 1900)
            {
                var page = new Page()
                {
                    Content = pageBuilder.ToString()
                };
                pages.Add(page);

                pageBuilder.Clear();
                pageNum++;
                pageBuilder.AppendLine($"Page {pageNum}");
            }

            pageBuilder.AppendLine(line);
        }
        
        
        var lastPage = new Page()
        {
            Content = pageBuilder.ToString()
        };
        pages.Add(lastPage);

        await ctx.Interaction.SendPaginatedResponseAsync(true, ctx.User, pages);
    }



    [Command("checkreuploads")]
    [Description("(Ephemeral) Shows if a mod has reuploaded content in its files.")]
    [RequireApplicationOwner]
    public async Task CheckModForReuploads(SlashCommandContext ctx,
        [Description("The web URL or Name ID for the mod")]
        string modUrlOrNameId)
    {
        if (ModioHelper.ModsClient is null)
        {
            await ctx.RespondAsync("The Mod.IO API clien't isn't initialized.\n" +
                                   "The operator of the bot needs to set an API key and/or OAuth2 token, and restart the bot."
                , true);
            return;
        }

        var modData = await ModioHelper.ModsClient.GetFromUrlOrNameId(modUrlOrNameId);
        if (modData is null)
        {
            await ctx.RespondAsync("Nothing found. Mod might not exist or the URL might not be a mod's?", true);
            return;
        }

        await ctx.RespondAsync($"Got it! Found {modData.Name}, now let me search for its files...", true);


        try
        {
            int counter = 0;
            await foreach (var modFile in ModioHelper.ModsClient[modData.Id].Files.Search().ToEnumerable())
            {
                counter++;
                await Task.Delay(1000);

                string platforms = string.Join(", ", modFile.Platforms.Select(p => p.Platform?.Value));
                string logTag = $"File {counter} ({modFile.Filename} for {platforms})";
                
                if (modFile.Download is null)
                {
                    await ctx.Interaction.RespondOrAppend($"Mod.IO's API sent null for {logTag}");
                    continue;
                }
                
                var zip = await GetZip(modFile.Download);
                if (zip is null)
                {
                    
                    await ctx.Interaction.RespondOrAppend($"Failed to download {logTag}");
                    continue;
                }
                
                var (foundBarcodes, foundHashes) = await ScanZipForReuploadedMods(zip, modData, false);

                if (foundBarcodes == 0 && foundHashes == 0)
                    await ctx.Interaction.RespondOrAppend($"Nothing found, looks like {logTag} is an **original** upload.");
                else 
                    await ctx.Interaction.RespondOrAppend($"Found {foundBarcodes} barcode(s) and {foundHashes} hash(es) in {logTag}");
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"Logger while checking {modData.Name} ({modData.NameId} #ID {modData.Id}) for reuploads", ex);
            await ctx.Interaction.RespondOrAppend($"Exception while checking files: {ex}");
        }

        await Task.Delay(1000);
        await ctx.Interaction.RespondOrAppend("Done!");
    }

    [Command("trustuser")]
    [Description("Controls what mod.io users are trusted to only post original mods.")]
    [RequireApplicationOwner]
    public async Task AutoCatalogModsFrom(SlashCommandContext ctx,
        [Description("True to add, false to remove.")] bool addOrRemove,
        [VariadicArgument(1)] params string[] nameIds)
    {
        int changes = 0;
        int noChanges = 0;
        
        // there's a better way to do this, but I don't really care. This works.

        // Returns: whether any change was made to the collection
        Func<string, bool> operation = addOrRemove
            ? static (modderId) =>
            {
                if (PersistentData.values.trustedModders.Contains(modderId))
                    return false;

                PersistentData.values.trustedModders.Add(modderId);
                return true;
            }
            : PersistentData.values.trustedModders.Remove;
        foreach (var nameId in nameIds)
        {
            if (operation(nameId))
                changes++;
            else
                noChanges++;
        }

        await ctx.RespondAsync(
            $"Done! {noChanges} no-change(s) and {changes} {(addOrRemove ? "addition(s)" : "removal(s)")}.\n" +
            $"There are now {PersistentData.values.trustedModders.Count} trusted modder(s).", true);
    }


    // [RequireApplicationOwner]
    // [Command("memoryhole")]
    // [Description("(Ephemeral) Forgets connections between given data and its associates.")]
    public async Task MemoryHole(SlashCommandContext ctx,
        [Description("Shows explainer on how this works")]
        bool seeExplainer = false,
        string? forgetBarcode = null,
        string? forgetAuthorNameId = null,
        params string[] forgetHashes)
    {
        if (seeExplainer)
        {
            await ctx.RespondAsync("This command fully removes mentions of any given piece of data.\n" +
                                   "For example, if you want `com.BaBaCorp.FliggolGiggul` removed, that barcode will " +
                                    "obviously be forgotten from the 'what barcodes were uploaded by who' list, but it " +
                                    "will also remove every hash that points to that barcode.\n" +
                                   "-# *(I'm not certain what the exact barcode is, I'm writing this on a 5 hour Alaska " +
                                    "Airlines flight and they charge $8 for WiFi per device)*\n" +
                                   "Similarly, if you want to memory-hole a mod author's entire catalog as Voidway has " +
                                    "cataloged it, just pass in their name id and Voidway will forget ever having seen " +
                                    "any mods from that user, and will also forget the hashes of mods they've posted.",
                true);
            return;
        }
        
        bool barcodeNull = string.IsNullOrWhiteSpace(forgetBarcode);
        bool authorNull = string.IsNullOrWhiteSpace(forgetAuthorNameId);
        bool anyHashes = forgetHashes.Length > 0;

        if (barcodeNull && authorNull && anyHashes)
        {
            await ctx.RespondAsync("Did you mean to check the explainer? You didn't send any barcode or author's " +
                                   "Name ID you wanted me to forget.", true);
            return;
        }
        if (barcodeNull ^ authorNull ^ anyHashes)
        {
            await ctx.RespondAsync("I'm only going to operate on one type of data at a time. " +
                                   "Please try again with one parameter type only.", true);
            return;
        }

        // Have to collect everything for removal first to avoid 
        List<string> hashesToRemove = [];
        HashSet<string> barcodesToRemove = [];

        if (!barcodeNull)
        {
            barcodesToRemove.Add(forgetBarcode!);
            await ctx.RespondAsync($"Gotcha, I'll round up every hash associated with {forgetBarcode}.", true);
        }
        else if (!authorNull)
        {
            
            await ctx.RespondAsync($"Gotcha, I'll round up every barcode associated with {forgetAuthorNameId}.", true);
            
            foreach (var kvp in PersistentData.values.barcodesToOriginalUploaders)
            {
                if (kvp.Key == forgetAuthorNameId)
                    barcodesToRemove.Add(kvp.Value);
            }

            await ctx.Interaction.RespondOrAppend($"Found {barcodesToRemove.Count} barcode(s) to remove.\n" +
                                                  $"Now rounding up hashes...");
        }
        else if (!anyHashes)
        {
            hashesToRemove.AddRange(forgetHashes);
        }

        foreach (var kvp in PersistentData.values.hashesToOriginalBarcodes)
        {
            if (barcodesToRemove.Contains(kvp.Key))
                hashesToRemove.Add(kvp.Value);
        }

        await ctx.Interaction.RespondOrAppend($"Found {hashesToRemove.Count} hash(es) to remove.\n" +
                                              $"Moving on to removing associations...");
        
        foreach (var removeHash in hashesToRemove)
        {
            PersistentData.values.hashesToOriginalBarcodes.Remove(removeHash);
        }

        foreach (var removeBarcode in barcodesToRemove)
        {
            PersistentData.values.barcodesToOriginalUploaders.Remove(removeBarcode);
        }
        
        PersistentData.WritePersistentData();

        if (!barcodeNull)
            await ctx.Interaction.RespondOrAppend($"Done! It's like {forgetBarcode} was never cataloged!");
        else if (!authorNull)
            await ctx.Interaction.RespondOrAppend($"Done! It's like {forgetAuthorNameId} was never cataloged!");
        
    }
}
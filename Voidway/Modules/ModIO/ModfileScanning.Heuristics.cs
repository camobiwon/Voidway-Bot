using System.IO.Compression;
using DSharpPlus.Entities;
using Modio.Models;

namespace Voidway.Modules.ModIO;

[Flags]
public enum ModContentHeuristic : ulong
{
    UnrecognizedNoMod = 0,
    MarrowMod = 1 << 0,
    Txt = 1 << 1,
    Img = 1 << 2,
    Blend = 1 << 3,
    Fbx = 1 << 4,
    Misc3dFile = 1 << 5,
    UnityPkg = 1 << 6,
    UnityProj = 1 << 7, // I swear to god this has happened a few times. a full fucking unity project.
    VirusFlagged = 1 << 10,
    Dll = 1 << 11,
    Zip = 1 << 12,
    FileTooLarge = 1 << 13,
    MarrowReplacer = 1 << 14,
    TiktokData = 1 << 15, // this has happened TWICE somehow.
    Installer = 1 << 16,
    Executable = 1 << 17,
    MacExecutableOrInstaller = 1 << 18,
    Video = 1 << 19,
    FailedBuild = 1 << 20,
    RobloxFile = 1 << 21, // This has happened at least once.
    UnrealProjectFiles  = 1 << 22, // This has also happened at least once.
    FilesOlderThan2Weeks = 1 << 23,
}

partial class ModfileScanning
{
    private static async Task ScanZipForHeuristics(ZipArchive zip, Mod modData)
    {
        var heuristics = ClassifyZipContents(zip);
        
        if (heuristics.HasFlag(ModContentHeuristic.MarrowMod) || heuristics.HasFlag(ModContentHeuristic.MarrowReplacer))
        {
            return;
        }
        
        DontAnnounceThese.Add(modData.Id);
        await AnnounceHeuristicResult(modData, heuristics);
    }
    
    private static async Task AnnounceHeuristicResult(Mod modData, ModContentHeuristic filenameHeuristic)
    {
        DiscordEmbedBuilder deb = new()
        {
            Author = new()
            {
                Url = modData.SubmittedBy?.ProfileUrl?.ToString(),
                Name = modData.SubmittedBy is not null ? $"{modData.SubmittedBy.Username} (ID: {modData.SubmittedBy.NameId})" : "??? (Mod.io API is fantastic and reliable)",
            },
            Description = "Mod files has/have: " + filenameHeuristic.ToString(),
            Title = $"{modData.Name} (ID: {modData.NameId})",
            Url = modData.ProfileUrl?.ToString()
        };
        
        foreach (var channel in Channels)
        {
            await channel.SendMessageAsync(deb.Build());
        }
        Logger.Put($"Announced in {Channels.Count} channel(s) that mod {modData.Name} ({modData.NameId}, #ID {modData.Id}) contains: {filenameHeuristic}");
    }
    
    private static ModContentHeuristic ClassifyZipContents(ZipArchive zip)
    {
        ModContentHeuristic ret = ModContentHeuristic.UnrecognizedNoMod;
        
        string[] textExts = [".txt", ".rtf", ".docx"];
        string[] imageExts = [".jpg", ".jpeg", ".png", ".webp", ".gif", ".tiff", ".bmp"];
        string[] misc3dExts = [".obj", ".stl", ".dae", ".glb", ".gltf"];
        string[] winExecExts = [".exe", ".bat", ".cmd", "ps1"];
        string[] winInstallerExts = [".msi", ".msix", ".appx", ".appxbundle"];
        string[] macAppAndInstallerExts = [".dmg", ".pkg", ".app", ".sh", ".command"];
        string[] videoExts = [".mp4", ".webm", ".avi", ".mov"];
        string[] robloxExts = [".rbxl", ".rbxlx", ".rbxm", ".rbxmx"];
        string[] unrealExts = [".uasset", ".uproject", ".umap"];

        string[] unityProjRoots = ["assets", "projectsettings", "packages",]; // only the explicitly required ones
            
        string[] filePaths = zip.Entries.Select(ze => ze.FullName.ToLower()).ToArray();
        HashSet<string> fileExtensions = filePaths.Select(Path.GetExtension).ToHashSet()!;
        DateTime oldestFileWriteTime = zip.Entries.Select(e => e.LastWriteTime).Min().DateTime;
        double oldestTimeDeltaDays = (DateTime.Now - oldestFileWriteTime).TotalDays;
        Logger.Put($"Mod file's oldest file is {oldestTimeDeltaDays:0.00} days old (from {oldestFileWriteTime})");
        
        bool hasBundle = fileExtensions.Contains(".bundle");
        bool hasJson = fileExtensions.Contains(".json"); // someone let that guy from Heavy Rain know
        bool hasHash = fileExtensions.Contains(".hash"); // isCalifornian? (haha get it? weed joke)
        bool isLikelyValidMod = hasBundle && hasJson && hasHash;
        bool isLikelyReplacerMod = !hasBundle && hasJson && hasHash;
        bool isFailedBuild = hasJson && !isLikelyReplacerMod && !isLikelyValidMod && filePaths.Any(p => p.Contains(".pallet."));
        bool hasTxt = textExts.Any(fileExtensions.Contains);
        bool hasImg = imageExts.Any(fileExtensions.Contains);
        bool hasBlend = fileExtensions.Contains(".blend");
        bool hasFbx = fileExtensions.Contains(".fbx");
        bool hasOther3d = filePaths.Any(p => misc3dExts.Contains(Path.GetExtension(p)));
        bool hasUnityPkg = fileExtensions.Contains(".unitypackage");
        bool hasUnityProj = fileExtensions.Contains(".meta") || unityProjRoots.All(root => filePaths.Any(p => p.StartsWith(root)));
        bool hasDll = fileExtensions.Contains(".dll");
        bool hasZip = fileExtensions.Contains(".zip");
        bool hasTiktokData = filePaths.Any(p => p.Replace(' ', '_').Contains("tiktok_data")); // this has somehow happened **twice**.
        bool hasInstaller = winInstallerExts.Any(fileExtensions.Contains);
        bool hasExecutable = winExecExts.Any(fileExtensions.Contains);
        bool hasMacExecOrInstaller = macAppAndInstallerExts.Any(fileExtensions.Contains);
        bool hasVideo = videoExts.Any(fileExtensions.Contains);
        bool hasRobloxFile = robloxExts.Any(fileExtensions.Contains); // a kid uploaded his Roblox "place" file. really.
        bool hasUnrealAssets = unrealExts.Any(fileExtensions.Contains); // a kid made a sandbox map or something in UE and uploaded the project
        bool hasOldFiles = oldestTimeDeltaDays > 14;
        
        if (isLikelyValidMod)
            ret |= ModContentHeuristic.MarrowMod;
        if (hasTxt)
            ret |= ModContentHeuristic.Txt;
        if (hasImg)
            ret |= ModContentHeuristic.Img;
        if (hasBlend)
            ret |= ModContentHeuristic.Blend;
        if (hasFbx)
            ret |= ModContentHeuristic.Fbx;
        if (hasOther3d)
            ret |= ModContentHeuristic.Misc3dFile;
        if (hasUnityPkg)
            ret |= ModContentHeuristic.UnityPkg;
        if (hasUnityProj)
            ret |= ModContentHeuristic.UnityProj;
        if (hasDll)
            ret |= ModContentHeuristic.Dll;
        if (hasZip)
            ret |= ModContentHeuristic.Zip;
        if (isLikelyReplacerMod)
            ret |= ModContentHeuristic.MarrowReplacer;
        if (hasTiktokData)
            ret |= ModContentHeuristic.TiktokData;
        if (hasInstaller)
            ret |= ModContentHeuristic.Installer;
        if (hasExecutable)
            ret |= ModContentHeuristic.Executable;
        if (hasMacExecOrInstaller)
            ret |= ModContentHeuristic.MacExecutableOrInstaller;
        if (hasVideo)
            ret |= ModContentHeuristic.Video;
        if (isFailedBuild)
            ret |= ModContentHeuristic.FailedBuild;
        if (hasRobloxFile)
            ret |= ModContentHeuristic.RobloxFile;
        if (hasUnrealAssets)
            ret |= ModContentHeuristic.UnrealProjectFiles;
        if (hasOldFiles)
            ret |= ModContentHeuristic.FilesOlderThan2Weeks;
        
        Logger.Put($"Detected {ret} from file extensions: {string.Join(", ", fileExtensions)}", LogType.Trace);

        return ret;
    }
}
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using DSharpPlus.Commands;
using DSharpPlus.Commands.ContextChecks;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

namespace Voidway.Modules;

[Command("mgmt")]
[RequireApplicationOwner]
public class BotManagement(Bot bot) : ModuleBase(bot)
{
    protected override async Task InitOneShot(GuildDownloadCompletedEventArgs args)
    {
        var client = bot.DiscordClient;
        if (client is null)
            return;
        var app = await client.GetCurrentApplicationAsync();
        Logger.Put($"Owners: {string.Join(", ", app.Owners ?? [])}", LogType.Debug);
        Logger.Put($"Team members: {string.Join(", ", app.Team?.Members ?? [])}", LogType.Debug);
        Logger.Put($"Team: {app.Team}", LogType.Debug);
    }

    [Command("reloadcfg")]
    [RequireApplicationOwner]
    public static async Task ReloadConfig(SlashCommandContext ctx)
    {
        await ctx.Interaction.RespondOrAppend("Reloading config and persistent data...");
        
        Config.ReadConfig();
        PersistentData.ReadPersistentData();
        
        await ctx.Interaction.RespondOrAppend("Done!");
    }
    
    
    [Command("updateRelaunch")]
    [Description("Pulls from git, rebuilds, and then relaunches the bot process")]
    [RequireApplicationOwner]
    public static async Task PullRelaunch(SlashCommandContext ctx)
    {
        await ctx.DeferResponseAsync(true);

        var gitOutput = "";
        var rootFolder = "";
        var relauncherPath = "";
        var dotnetRestoreOutput = "";
        var dotnetBuildOutput = "";
        Exception? exception = null;

        Logger.Put($"Pulling from git and then relaunching at the request of {ctx.User.Username}#{ctx.User.Discriminator} (ID={ctx.User.Id})");

        await Task.Run(() =>
        {
            try
            {
                var proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "git",
                        Arguments = "pull",
                        CreateNoWindow = true,
                        RedirectStandardOutput = true
                    }
                };
                Logger.Put($"Executing command: {proc.StartInfo.FileName} {proc.StartInfo.Arguments}", LogType.Debug);
                proc.Start();
                proc.WaitForExit();
                gitOutput = proc.StandardOutput.ReadToEnd();
                Logger.Put($"Output from {proc.StartInfo.FileName} {proc.StartInfo.Arguments}: {gitOutput}", LogType.Debug);

                proc.StartInfo.Arguments = "rev-parse --show-toplevel";
                Logger.Put($"Executing command: {proc.StartInfo.FileName} {proc.StartInfo.Arguments}", LogType.Debug);
                proc.Start();
                proc.WaitForExit();
                rootFolder = proc.StandardOutput.ReadToEnd().TrimEnd();
                Logger.Put($"Output from {proc.StartInfo.FileName} {proc.StartInfo.Arguments}: {rootFolder}", LogType.Debug);

                proc.StartInfo.FileName = "dotnet";
                proc.StartInfo.Arguments = "restore";
                proc.StartInfo.WorkingDirectory = rootFolder;
                Logger.Put($"Executing command: {proc.StartInfo.FileName} {proc.StartInfo.Arguments}", LogType.Debug);
                proc.Start();
                proc.WaitForExit();
                dotnetRestoreOutput = proc.StandardOutput.ReadToEnd();
                Logger.Put($"Output from {proc.StartInfo.FileName} {proc.StartInfo.Arguments}: {dotnetRestoreOutput}", LogType.Debug);

                proc.StartInfo.Arguments = "build \"Relauncher/Relauncher.csproj\"";
                Logger.Put($"Executing command: {proc.StartInfo.FileName} {proc.StartInfo.Arguments}", LogType.Debug);
                proc.Start();
                proc.WaitForExit();
                dotnetBuildOutput = proc.StandardOutput.ReadToEnd();
                Logger.Put($"Output from {proc.StartInfo.FileName} {proc.StartInfo.Arguments}: {dotnetBuildOutput}", LogType.Debug);
                // trying to get line: "  Voidway Bot Relauncher -> C:\Users\extraes\source\repos\Voidway-Bot\Relauncher\bin\Debug\net7.0\Voidway Bot Relauncher.dll"
                // and stop before line: "Build succeeded."
                relauncherPath = dotnetBuildOutput.Replace("\r\n", "\n").Split('>')[1].Split("\n\n")[0].Trim().Replace("\n  ", "");
                if (OperatingSystem.IsWindows())
                    relauncherPath = Path.ChangeExtension(relauncherPath, "exe");
                else
                    relauncherPath = Path.ChangeExtension(relauncherPath, null).TrimEnd('.');

                Logger.Put($"Found relauncher path to be {relauncherPath}", LogType.Debug);
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });


        var entryPoint = Assembly.GetEntryAssembly();
        if (entryPoint is null)
        {
            Logger.Error("Error while attempting to start new bot process",
                new EntryPointNotFoundException("This process has no executable/executing assembly! This is not allowed!"));
            DiscordWebhookBuilder bldr = new()
            {
                Content = "Unable to find entry-point of current process."
            };
            await ctx.EditResponseAsync(bldr);
            return;
        }

        StringBuilder outputMessage = new();

        gitOutput = gitOutput.TrimEnd();
        dotnetRestoreOutput = dotnetRestoreOutput.TrimEnd();
        dotnetBuildOutput = dotnetBuildOutput.TrimEnd();

        if (string.IsNullOrEmpty(gitOutput))
        {
            outputMessage.AppendLine("Unable to pull from git remote. ");
            if (exception is not null)
                outputMessage.Append(exception);
            goto SENDMESSAGE;
        }

        outputMessage.AppendLine($"Git pull results:\n{gitOutput}");

        if (string.IsNullOrEmpty(dotnetRestoreOutput))
        {
            outputMessage.AppendLine("Unable to run `dotnet restore`. ");
            if (exception is not null)
                outputMessage.Append(exception);
            goto SENDMESSAGE;
        }

        outputMessage.AppendLine($"Results of `dotnet restore`:\n{dotnetRestoreOutput}");

        if (string.IsNullOrEmpty(dotnetBuildOutput))
        {
            outputMessage.AppendLine("Unable to get remote-to-local commit diff names. ");
            if (exception is not null)
                outputMessage.Append(exception);
        }
        else outputMessage.AppendLine($"Results of `dotnet build` for relauncher:\n{dotnetBuildOutput}");

        // bro this is such shit code never use labels EVER bruh
        SENDMESSAGE:
        DiscordWebhookBuilder dwb = new()
        {
            Content = Logger.EnsureShorterThan(outputMessage.ToString(), 2000, "\n[cutoff for discord]")
        };
        await ctx.EditResponseAsync(dwb);

        if (exception is not null || dotnetBuildOutput.Contains("Build FAILED.")) return;

        Process relauncher = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = relauncherPath,
                CreateNoWindow = false
            }
        };

        var currExecLocation = entryPoint.Location;
        if (OperatingSystem.IsWindows()) currExecLocation = Path.ChangeExtension(currExecLocation, "exe");
        else currExecLocation = Path.ChangeExtension(currExecLocation, null).TrimEnd('.');

        RelaunchParameters parms = new()
        {
            buildProject = Path.Combine(rootFolder, "Voidway", "Voidway.csproj"),
            launchWorkingDir = Environment.CurrentDirectory,
            launchExecutable = currExecLocation,
            initiatorId = ctx.User.Id
        };

        foreach (var parm in parms.BuildLaunchParameters())
        {
            relauncher.StartInfo.ArgumentList.Add(parm);
        }

        Logger.Put(
            $"Created relauncher process, not yet started. The following command will be ran: {relauncher.StartInfo.FileName} \"{string.Join("\" \"", relauncher.StartInfo.ArgumentList)}\"",
            LogType.Debug);
        try
        {
            relauncher.Start();
        }
        catch (Exception ex)
        {
            Logger.Error("Exception thrown when attempting to start relauncher process, aborting!", ex);
            DiscordFollowupMessageBuilder dfmb = new()
            {
                Content = "Exception thrown when attempting to start relauncher process, aborting:\n\t" + ex,
                IsEphemeral = true
            };
            await ctx.FollowupAsync(dfmb);
            return;
        }

        Logger.Put("Relauncher process started. The current bot process will now exit.");
        Environment.Exit(0);
    }
    
    
    [Command("getLogs")]
    [Description("Returns the last logs that'll fit in ~2000 characters")]
    [RequireApplicationOwner]
    public static async Task GetLogs(SlashCommandContext ctx,
        [Description("Only return logs that contain a specific string.")]
        string? filterFor = null,
        [Description("Only return logs that DON'T contain a specific string.")]
        string? filterOut = null,
        [Description("Reverses the order of log statements. Default=true.")]
        bool newestFirst = true)
    {
        StringBuilder sb = new();
        var collection = newestFirst ? Logger.LogStatements.Reverse() : Logger.LogStatements;
        foreach (var nextStr in collection)
        {
            if (filterFor is not null && !nextStr.Contains(filterFor, StringComparison.InvariantCultureIgnoreCase))
                continue;
            if (filterOut is not null && nextStr.Contains(filterOut, StringComparison.InvariantCultureIgnoreCase))
                continue;

            var newStr = Formatter.Sanitize(nextStr);
            if (sb.Length + newStr.Length > 2000)
                break;
            sb.AppendLine(newStr);
        }

        var str = sb.Length == 0 ? "-# No logs returned, try changing your filters?" : sb.ToString();
        await ctx.RespondAsync(str, true);
    }
}
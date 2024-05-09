using System.Linq.Expressions;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus;
using DSharpPlus.SlashCommands;
using DSharpPlus.SlashCommands.Attributes;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using DSharpPlus.Interactivity.Extensions;

namespace Voidway_Bot
{
    internal class DebugCommands : ApplicationCommandModule
    {

        [SlashCommand("addowner", "Adds a user to the list of owners")]
        [SlashRequireOwner]
        public async Task AddOwner(
            InteractionContext ctx,
            [Option("user", "Discord user to add to owners list.")]
            DiscordUser user
            )
        {
            ulong id = user.Id;
            string userStr = $"{user.Username}#{user.Discriminator} (ID={id})";
            DiscordFollowupMessageBuilder followup = new()
            {
                IsEphemeral = true,
                Content = $"Added {id} to the list of owners!"
            };

            await ctx.CreateResponseAsync("You'd better be ABSOLUTELY certain about this!", true);

            await Config.ModifyConfig(vals => vals.owners = vals.owners.Concat(new ulong[] { id }).ToArray());
            await ctx.FollowUpAsync(followup);
        }

        [SlashCommand("getcommit", "Get the current git commit. Requires Git to be installed and the executable within the repository")]
        [SlashRequireVoidwayOwner]
        public async Task GetCommit(InteractionContext ctx)
        {
            await ctx.DeferAsync(true);

            string commitCount = "";
            string aheadCountOutput = "";
            bool aheadOfRemote = false;
            string commitNames = "";
            string showResult = "";


            Exception? exception = null;

            Logger.Put($"Executing git commit commands for bot owner {ctx.User.Username}#{ctx.User.Discriminator}");
            await Task.Run(() =>
            {
                // this is such shit code but i basically just fucking lifted it from 
                try
                {
                    const string BRANCH_NAME = "master"; // ...semihardcoding?

                    Process git = new Process()
                    {
                        StartInfo = new ProcessStartInfo()
                        {
                            FileName = "git",
                            Arguments = "fetch",
                            CreateNoWindow = true,
                            RedirectStandardOutput = true,
                        },
                    };

                    Logger.Put($"Executing command: {git.StartInfo.FileName} {git.StartInfo.Arguments}", Logger.Reason.Debug);
                    git.Start();
                    git.WaitForExit();
                    string fetchOutput = git.StandardOutput.ReadToEnd().TrimEnd();
                    Logger.Put($"Output from {git.StartInfo.FileName} {git.StartInfo.Arguments}: {fetchOutput}", Logger.Reason.Debug);

                    git.StartInfo.Arguments = "rev-list --all --count";
                    Logger.Put($"Executing command: {git.StartInfo.FileName} {git.StartInfo.Arguments}", Logger.Reason.Debug);
                    git.Start();
                    git.WaitForExit();
                    commitCount = git.StandardOutput.ReadToEnd().TrimEnd();
                    Logger.Put($"Output from {git.StartInfo.FileName} {git.StartInfo.Arguments}: {commitCount}", Logger.Reason.Debug);

                    git.StartInfo.Arguments = $"rev-list {BRANCH_NAME} --not origin/{BRANCH_NAME} --count";
                    git.Start();
                    git.WaitForExit();
                    aheadCountOutput = git.StandardOutput.ReadToEnd().TrimEnd();
                    if (aheadCountOutput != "0") aheadOfRemote = true;
                    else
                    {
                        git.StartInfo.Arguments = $"rev-list origin/{BRANCH_NAME} --not {BRANCH_NAME} --count"; //thisll need to change if we ever add a diff remote branch
                        git.Start();
                        git.WaitForExit();
                        aheadOfRemote = false;
                    }

                    if (aheadOfRemote)
                        git.StartInfo.Arguments = $"rev-list {BRANCH_NAME} --not origin/{BRANCH_NAME} --pretty=short";
                    else
                        git.StartInfo.Arguments = $"rev-list origin/{BRANCH_NAME} --not {BRANCH_NAME} --pretty=short";
                    Logger.Put($"Executing command: {git.StartInfo.FileName} {git.StartInfo.Arguments}", Logger.Reason.Debug);
                    git.Start();
                    git.WaitForExit();
                    commitNames = git.StandardOutput.ReadToEnd().TrimEnd();
                    Logger.Put($"Output from {git.StartInfo.FileName} {git.StartInfo.Arguments}: {commitNames}", Logger.Reason.Debug);

                    git.StartInfo.Arguments = $"show -s";
                    Logger.Put($"Executing command: {git.StartInfo.FileName} {git.StartInfo.Arguments}", Logger.Reason.Debug);
                    git.Start();
                    git.WaitForExit();
                    showResult = git.StandardOutput.ReadToEnd().TrimEnd();
                    Logger.Put($"Output from {git.StartInfo.FileName} {git.StartInfo.Arguments}: {showResult}", Logger.Reason.Debug);
                }
                catch (Exception ex)
                {
                    exception = ex;
                }
            });

            StringBuilder outputMessage = new();

            if (string.IsNullOrEmpty(commitCount))
            {
                outputMessage.AppendLine("Unable to get commit count. ");
                if (exception is not null)
                    outputMessage.Append(exception.ToString());
                goto SENDMESSAGE;
            }
            else outputMessage.AppendLine($"There have been {commitCount} commits since this repository's creation.");

            if (string.IsNullOrEmpty(aheadCountOutput))
            {
                outputMessage.AppendLine("Unable to get remote-to-local commit diff count.");
                if (exception is not null)
                    outputMessage.Append(exception.ToString());
                goto SENDMESSAGE;
            }
            else outputMessage.AppendLine($"There have been {aheadCountOutput} commits between local and remote (and likely since last build). Local is {(aheadOfRemote ? "ahead of" : "behind")} remote.");

            if (string.IsNullOrEmpty(commitNames))
            {
                outputMessage.AppendLine("Unable to get remote-to-local commit diff names.");
                if (exception is not null)
                    outputMessage.Append(exception.ToString());
                goto SENDMESSAGE;
            }
            else
            {
                //var commits = commitNames.Split('\n', StringSplitOptions.TrimEntries).Select(cn => $"({cn[..7]}) - {cn}");
                //outputMessage.AppendLine($"Here's a list of the commits between the two:\n\t{string.Join("\n\t", commits.ToArray())}");
                outputMessage.AppendLine($"Here's a list of the commits between the two:\n{commitNames}");
            }

            if (string.IsNullOrEmpty(showResult))
            {
                outputMessage.AppendLine("Unable to get commit details.");
                if (exception is not null)
                    outputMessage.Append(exception.ToString());
                goto SENDMESSAGE;
            }
            else outputMessage.AppendLine($"Here's the details of the current commit:\n{showResult}");

        // bro this is such shit code never use labels EVER bruh
        SENDMESSAGE:
            DiscordWebhookBuilder dwb = new()
            {
                Content = outputMessage.ToString(),
            };
            await ctx.EditResponseAsync(dwb);
        }

        [SlashCommand("updaterelaunch", "Pulls from git, rebuilds, and then relaunches the bot process")]
        [SlashRequireVoidwayOwner]
        public async Task PullRelaunch(InteractionContext ctx)
        {
            await ctx.DeferAsync(true);

            string gitOutput = "";
            string rootFolder = "";
            string relauncherPath = "";
            string dotnetRestoreOutput = "";
            string dotnetBuildOutput = "";
            Exception? exception = null;

            Logger.Put($"Pulling from git and then relaunching at the request of {ctx.User.Username}#{ctx.User.Discriminator} (ID={ctx.User.Id})");

            await Task.Run(() =>
            {
                try
                {
                    Process proc = new Process()
                    {
                        StartInfo = new ProcessStartInfo()
                        {
                            FileName = "git",
                            Arguments = "pull",
                            CreateNoWindow = true,
                            RedirectStandardOutput = true,
                        },
                    };
                    Logger.Put($"Executing command: {proc.StartInfo.FileName} {proc.StartInfo.Arguments}", Logger.Reason.Debug);
                    proc.Start();
                    proc.WaitForExit();
                    gitOutput = proc.StandardOutput.ReadToEnd();
                    Logger.Put($"Output from {proc.StartInfo.FileName} {proc.StartInfo.Arguments}: {gitOutput}", Logger.Reason.Debug);

                    proc.StartInfo.Arguments = "rev-parse --show-toplevel";
                    Logger.Put($"Executing command: {proc.StartInfo.FileName} {proc.StartInfo.Arguments}", Logger.Reason.Debug);
                    proc.Start();
                    proc.WaitForExit();
                    rootFolder = proc.StandardOutput.ReadToEnd().TrimEnd();
                    Logger.Put($"Output from {proc.StartInfo.FileName} {proc.StartInfo.Arguments}: {rootFolder}", Logger.Reason.Debug);

                    proc.StartInfo.FileName = "dotnet";
                    proc.StartInfo.Arguments = "restore";
                    proc.StartInfo.WorkingDirectory = rootFolder;
                    Logger.Put($"Executing command: {proc.StartInfo.FileName} {proc.StartInfo.Arguments}", Logger.Reason.Debug);
                    proc.Start();
                    proc.WaitForExit();
                    dotnetRestoreOutput = proc.StandardOutput.ReadToEnd();
                    Logger.Put($"Output from {proc.StartInfo.FileName} {proc.StartInfo.Arguments}: {dotnetRestoreOutput}", Logger.Reason.Debug);

                    proc.StartInfo.Arguments = "build \"Relauncher/Voidway Bot Relauncher.csproj\"";
                    Logger.Put($"Executing command: {proc.StartInfo.FileName} {proc.StartInfo.Arguments}", Logger.Reason.Debug);
                    proc.Start();
                    proc.WaitForExit();
                    dotnetBuildOutput = proc.StandardOutput.ReadToEnd();
                    Logger.Put($"Output from {proc.StartInfo.FileName} {proc.StartInfo.Arguments}: {dotnetBuildOutput}", Logger.Reason.Debug);
                    // trying to get line: "  Voidway Bot Relauncher -> C:\Users\extraes\source\repos\Voidway-Bot\Relauncher\bin\Debug\net7.0\Voidway Bot Relauncher.dll"
                    // and stop before line: "Build succeeded."
                    relauncherPath = dotnetBuildOutput.Replace("\r\n", "\n").Split('>')[1].Split("\n\n")[0].Trim().Replace("\n  ", "");
                    if (OperatingSystem.IsWindows()) relauncherPath = Path.ChangeExtension(relauncherPath, "exe");
                    else relauncherPath = Path.ChangeExtension(relauncherPath, null).TrimEnd('.');

                    Logger.Put($"Found relauncher path to be {relauncherPath}", Logger.Reason.Debug);
                }
                catch (Exception ex)
                {
                    exception = ex;
                }
            });


            Assembly? entryPoint = Assembly.GetEntryAssembly();
            if (entryPoint is null)
            {
                Logger.Error("Error while attempting to start new bot process", new EntryPointNotFoundException("This process has no executable/executing assembly! This is not allowed!"));
                DiscordWebhookBuilder bldr = new()
                {
                    Content = "Unable to find entry-point of current process.",
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
                    outputMessage.Append(exception.ToString());
                goto SENDMESSAGE;
            }
            else outputMessage.AppendLine($"Git pull results:\n{gitOutput}");

            if (string.IsNullOrEmpty(dotnetRestoreOutput))
            {
                outputMessage.AppendLine("Unable to run `dotnet restore`. ");
                if (exception is not null)
                    outputMessage.Append(exception.ToString());
                goto SENDMESSAGE;
            }
            else outputMessage.AppendLine($"Results of `dotnet restore`:\n{dotnetRestoreOutput}");

            if (string.IsNullOrEmpty(dotnetBuildOutput))
            {
                outputMessage.AppendLine("Unable to get remote-to-local commit diff names. ");
                if (exception is not null)
                    outputMessage.Append(exception.ToString());
                goto SENDMESSAGE;
            }
            else outputMessage.AppendLine($"Results of `dotnet build` for relauncher:\n{dotnetBuildOutput}");

            // bro this is such shit code never use labels EVER bruh
            SENDMESSAGE:
            DiscordWebhookBuilder dwb = new()
            {
                Content = outputMessage.ToString(),
            };
            await ctx.EditResponseAsync(dwb);

            if (exception is not null || dotnetBuildOutput.Contains("Build FAILED.")) return;

            Process relauncher = new()
            {
                StartInfo = new()
                {
                    FileName = relauncherPath,
                    CreateNoWindow = false
                }
            };

            string voidwayBotPath = entryPoint.Location;
            if (OperatingSystem.IsWindows()) voidwayBotPath = Path.ChangeExtension(voidwayBotPath, "exe");
            else voidwayBotPath = Path.ChangeExtension(voidwayBotPath, null).TrimEnd('.');

            relauncher.StartInfo.ArgumentList.Add("RF=" + rootFolder);
            relauncher.StartInfo.ArgumentList.Add(voidwayBotPath);
            relauncher.StartInfo.ArgumentList.Add(ctx.User.Id.ToString());
            if (Debugger.IsAttached) relauncher.StartInfo.ArgumentList.Add("DEBUGGING");
            Logger.Put($"Created relauncher process, not yet started. The following command will be ran: {relauncher.StartInfo.FileName} \"{string.Join("\" ", relauncher.StartInfo.ArgumentList)}\"", Logger.Reason.Debug);
            try 
            {
                relauncher.Start();
            }
            catch(Exception ex)
            {
                Logger.Error("Exception thrown when attemping to start relauncher process, aborting!", ex);
                DiscordFollowupMessageBuilder dfmb = new()
                {
                    Content = "Exception thrown when attempting to start relauncher process, aborting:\n\t" + ex.ToString(),
                    IsEphemeral = true,
                };
                await ctx.FollowUpAsync(dfmb);
                return;
            }

            Logger.Put("Relauncher process started. The current bot process will now exit.");
            Environment.Exit(0);
        }

        public static void HandleRelaunch()
        {
            if (Bot.Args.Length == 0 || !Bot.Args.Contains("UPDATED")) return;

            // do insane arg passing to pass debugger state between processes and restarts
            if (Bot.Args.Contains("DEBUGGING")) Debugger.Launch();

            Logger.Put("Voidway Bot restarted after updated from owner command!");

            Bot.CurrClient.SessionCreated += RelaunchThunk;
        }

        private static async Task RelaunchThunk(DiscordClient sender, SessionReadyEventArgs e)
        {
            try
            {
                Bot.CurrClient.SessionCreated -= RelaunchThunk;
            }
            catch { }

            // readback args passed from relauncher process
            try
            {
                string? uid = Bot.Args.FirstOrDefault(arg => ulong.TryParse(arg, out ulong _));
                string? buildOutput = Bot.Args.FirstOrDefault(arg => arg.Contains("MSBuild version"));
                if (uid is null) return;
                Logger.Put($"Fetching user w/ ID={uid} to DM post-restart");

                ulong userId = ulong.Parse(uid);
                await Bot.CurrClient.GetUserAsync(userId);
                // have to do this bullshit to be able to DM the user
                ConstructorInfo ctor = typeof(DiscordMember).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, Array.Empty<Type>())!;
                DiscordMember member = (DiscordMember)ctor.Invoke(Array.Empty<object>());
                typeof(DiscordMember).GetProperty("Discord", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(member, Bot.CurrClient);
                typeof(DiscordMember).GetProperty("Id")!.SetValue(member, userId);
                await member.SendMessageAsync("Voidway Bot restarted successfully.\n`dotnet build` output:\n" + (buildOutput ?? "<none (what?)>"));
            }
            catch (Exception ex)
            {
                Logger.Warn("Failed to execute a relaunch callback: ", ex);
            }

            Logger.Put("Finished relaunch callback.");
        }

        [SlashCommand("getlogs", "Retrieves the most logs that will fit into a 2000 char message.")]
        [SlashRequireVoidwayOwner]
        private static async Task GetLogs(
            InteractionContext ctx)
            //[Option("Reverse", "Shows the reverse (start?) of the logs instead of its default order")]
            //bool reverse)
        {
            const bool reverse = false;

            StringBuilder sb = new(2000);
            if (Logger.logStatements.IsEmpty)
            {
                await ctx.CreateResponseAsync("There have been no recent logs.", true);
                return;
            }

            Logger.Put($"Dumping logs to {ctx.User.Username}#{ctx.User.Discriminator} (ID={ctx.User.Id}), Reversed?={reverse}");

            var logs = reverse ? Logger.logStatements : Logger.logStatements.Reverse();
            foreach (string log in logs)
            {
                if (log.Length + sb.Length >= 2000) break;
                sb.AppendLine(log);
            }

            await ctx.CreateResponseAsync(sb.ToString(), true);
        }

        [SlashCommand("uptime", "Retrieves the length of time this instance of Voidway Bot has been active for.")]
        [SlashRequireUserPermissions(Permissions.ManageMessages)]
        private async Task GetUptime(InteractionContext ctx)
        {
            Process proc = Process.GetCurrentProcess();

            TimeSpan uptime = DateTime.Now - proc.StartTime;
            try
            {
                await ctx.DeferAsync(true);
                var dfmb = new DiscordFollowupMessageBuilder()
                    .WithContent($"{uptime.Days}d {uptime.Hours}h {uptime.Minutes}m {uptime.Seconds}s")
                    .AsEphemeral(true);
                await ctx.FollowUpAsync(dfmb);
                //await ctx.CreateResponseAsync($"{uptime.Days}d {uptime.Hours}h {uptime.Minutes}m {uptime.Seconds}s", true);
            }
            catch (Exception ex)
            {
                Logger.Error("Error while attempting to send uptime", ex);
            }
        }

        [SlashCommand("pid", "Sends PID in msg(s), so don't use in normal chats.")]
        [SlashRequireUserPermissions(Permissions.ManageMessages)]
        private async Task SendPid(InteractionContext ctx)
        {
            Process proc = Process.GetCurrentProcess();
            try
            {
                await ctx.Channel.SendMessageAsync($"PID: {proc.Id}");

                await ctx.CreateResponseAsync($"hi Bro.", true);
            }
            catch { }
        }
    }
}

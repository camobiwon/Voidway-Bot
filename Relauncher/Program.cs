using System.Diagnostics;

namespace Voidway_Bot
{
    internal class Program
    {
        static async void Main(string[] args)
        {
            if (args.Contains("DEBUGGING")) Debugger.Launch();

            await Task.Delay(1000); // wait for main proc to finish closing to release locks

            // dont even bother try-catching. if this fails we're basically fucked (and shouldn'tve gotten this far in the first place)
            string rootFolder = args[0];
            string voidwayBotEntry = args[1];
            string dotnetBuildOutput;

            Process proc = new()
            {
                StartInfo = new()
                {
                    FileName = "dotnet",
                    Arguments = "build \"Voidway Bot.csproj\"",
                    WorkingDirectory = rootFolder,
                    RedirectStandardOutput = true,
                }
            };

            proc.Start();
            proc.WaitForExit();
            dotnetBuildOutput = proc.StandardOutput.ReadToEnd();

            proc.StartInfo.FileName = voidwayBotEntry;
            proc.StartInfo.RedirectStandardOutput = false;
            proc.StartInfo.WorkingDirectory = Path.GetDirectoryName(voidwayBotEntry);
            proc.StartInfo.WindowStyle = ProcessWindowStyle.Normal;
            proc.StartInfo.CreateNoWindow = false;
            proc.StartInfo.Arguments = "";
            proc.StartInfo.ArgumentList.Add("UPDATED");
            proc.StartInfo.ArgumentList.Add(dotnetBuildOutput);
            proc.StartInfo.ArgumentList.Add(args[2]);
            if (args.Contains("DEBUGGING")) proc.StartInfo.ArgumentList.Add("DEBUGGING");
            proc.Start();
            Environment.Exit(0);
        }
    }
}
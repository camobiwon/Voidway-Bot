using System.Diagnostics;

namespace Voidway_Bot
{
    internal class Program
    {
        static void Main(string[] args)
        {
            if (args.Contains("DEBUGGING")) Debugger.Launch();

            // wait for main proc to finish closing to release locks
            Thread.Sleep(1000); // ideally id await but apparently main cant be async.

            // dont even bother try-catching. if this fails we're basically fucked (and shouldn'tve gotten this far in the first place)
            string rootFolder = args[0];
            string voidwayBotEntry = args.First(arg => Path.GetFileNameWithoutExtension(arg).EndsWith("Voidway Bot"));
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

            proc.StartInfo.RedirectStandardOutput = false;
            proc.StartInfo.WorkingDirectory = Path.GetDirectoryName(voidwayBotEntry);
            proc.StartInfo.WindowStyle = ProcessWindowStyle.Normal;
            proc.StartInfo.CreateNoWindow = false;
            proc.StartInfo.Arguments = "run " + voidwayBotEntry;
            proc.StartInfo.ArgumentList.Add("UPDATED");
            proc.StartInfo.ArgumentList.Add(dotnetBuildOutput);
            proc.StartInfo.ArgumentList.Add(args.First(arg => ulong.TryParse(arg, out _));
            if (args.Contains("DEBUGGING")) proc.StartInfo.ArgumentList.Add("DEBUGGING");
            proc.Start();
            Environment.Exit(0);
        }
    }
}
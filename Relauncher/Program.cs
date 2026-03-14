using System.Diagnostics;

namespace Relauncher;

class Program
{
    static void Main(string[] args)
    {
        Thread.Sleep(1000); // make sure parent process is dead
        
        var parms = RelaunchParameters.Parse(args)!;
        // string buildProject = args.First(arg => arg.StartsWith("BUILD=")).Replace("BUILD=", ""); // full path
        // string continueWith = args.First(arg => arg.StartsWith("CONT=")).Replace("CONT=", ""); // full path
        // string continueRootFolder = args.First(arg => arg.StartsWith("PWD=")).Replace("PWD=", "");
        // string? initiatorId = args.FirstOrDefault(arg => arg.StartsWith("USERID=")); // optional

        string projFolder = Path.GetDirectoryName(parms.buildProject) ?? Environment.CurrentDirectory;
        string projFile = Path.GetFileName(parms.buildProject)!;
        
        string configuration = string.IsNullOrWhiteSpace(parms.buildConfiguration) ? "" : $" --configuration \"{parms.buildConfiguration}\"";
        string dotnetBuildOutput;
        Process proc = new()
        {
            StartInfo = new()
            {
                FileName = "dotnet",
                Arguments = $"build \"{projFile}\"{configuration}",
                WorkingDirectory = projFolder,
                RedirectStandardOutput = true,
            }
        };

        string buildInvocation = $"{proc.StartInfo.FileName} {proc.StartInfo.Arguments} [in {projFolder}]";
        
        proc.Start();
        proc.WaitForExit();
        dotnetBuildOutput = proc.StandardOutput.ReadToEnd().Replace(projFolder, "$PWD");

        proc = new()
        {
            StartInfo = new()
            {
                FileName = parms.launchExecutable,
                WorkingDirectory = parms.launchWorkingDir
            }
        };

        string runCommandsParameter = $"### {buildInvocation}\n{dotnetBuildOutput}";
        proc.StartInfo.ArgumentList.Add(RelaunchParameters.RELAUNCHED_ARG);
        proc.StartInfo.ArgumentList.Add(runCommandsParameter);
        if (parms.initiatorId.HasValue)
            proc.StartInfo.ArgumentList.Add("USERID=" + parms.initiatorId.Value);

        proc.Start();
        Console.WriteLine("DONE, NOW LAUNCHING BOT!!!");
        Console.WriteLine("DONE, NOW LAUNCHING BOT!!!");
        Console.WriteLine("DONE, NOW LAUNCHING BOT!!!");
        Console.WriteLine("DONE, NOW LAUNCHING BOT!!!");
        Console.WriteLine("DONE, NOW LAUNCHING BOT!!!");
        
        Console.WriteLine($"Executing command: {proc.StartInfo.FileName} \"{string.Join("\" \"", proc.StartInfo.ArgumentList)}\"");
        Environment.Exit(0);
    }
}
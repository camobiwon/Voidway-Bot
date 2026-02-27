using System.Runtime.CompilerServices;

namespace Voidway;

public static class Program
{
    // ReSharper disable once MemberCanBePrivate.Global
    public static Bot bot = null!;
    
    [MethodImpl(MethodImplOptions.NoOptimization)]
    static async Task Main(string[] args)
    {
        // setup process logs
        AppDomain.CurrentDomain.UnhandledException += LogUnhandledException;
        Console.CancelKeyPress += Console_CancelKeyPress;
        
        Logger.Put("Pre-initializing persistent data...");
        _ = PersistentData.values;

        if (string.IsNullOrWhiteSpace(Config.values.discordToken))
        {
            for (int i = 0; i < 5; i++)
            {
                Logger.Put("HI PLEASE PUT IN A TOKEN IN THE NEWLY GENERATED CONFIG FILE THX!!! :)");
                Logger.Error("HI PLEASE PUT IN A TOKEN IN THE NEWLY GENERATED CONFIG FILE THX!!! :)");
            }

            return;
        }
        
        Logger.Put("Initializing bot...");
        bot = new Bot(Config.values.discordToken);
        await bot.ConnectAsync();

        while(true)
        {
            await Task.Delay(60 * 1000);
            // Let execution loop in case I ever want to attach a remote debugger and need a place to put a breakpoint 
        }
        // ReSharper disable once FunctionNeverReturns
    }
    
    private static void Console_CancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        Logger.Error("Operator pressed Ctrl+C, exiting now.");
        Console.WriteLine();
        Environment.Exit(0);
    }

    private static void LogUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        Console.WriteLine();
        var exc = e.ExceptionObject as Exception;
        Logger.Error(exc?.ToString() ?? "Unknown exception");
        Environment.Exit(0);
    }
}
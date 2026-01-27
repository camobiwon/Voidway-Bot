namespace Voidway;

public class Program
{
    static void Main(string[] args)
    {
        // setup process logs
        AppDomain.CurrentDomain.UnhandledException += LogUnhandledException;
        Console.CancelKeyPress += Console_CancelKeyPress;
        
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
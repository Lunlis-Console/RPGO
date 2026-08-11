using System;

namespace LostAndDivine.ClientMonoGame;

public static class Program
{
    [STAThread]
    static void Main()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var ex = e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString());
            Logger.Crash("UNHANDLED EXCEPTION (UnhandledException)", ex);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            e.SetObserved();
            Logger.Crash("UNOBSERVED TASK EXCEPTION", e.Exception);
        };

        using var game = new GameMain();
        try
        {
            game.Run();
        }
        catch (Exception ex)
        {
            Logger.Crash("CRASH (game.Run)", ex);
            throw;
        }
    }
}

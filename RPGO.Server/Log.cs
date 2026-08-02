using Serilog;
using System.Text;

namespace RPGGame.Server;

public static class Log
{
    private static Serilog.ILogger _logger = null!;

    public static void Init()
    {
        _logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.WithProperty("Application", "RPGO.Server")
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File("logs/server-.log",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                encoding: Encoding.UTF8,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }

    // Ленивый fallback: если Init() не вызывался (например, в юнит-тестах) — пишем в консоль.
    private static Serilog.ILogger Logger
    {
        get
        {
            if (_logger == null)
            {
                lock (typeof(Log))
                {
                    if (_logger == null)
                    {
                        _logger = new LoggerConfiguration()
                            .MinimumLevel.Debug()
                            .WriteTo.Console()
                            .CreateLogger();
                    }
                }
            }
            return _logger;
        }
    }

    public static void Info(string message) => ConsoleManager.WriteLog(() => Logger.Information(message));
    public static void Warn(string message) => ConsoleManager.WriteLog(() => Logger.Warning(message));
    public static void Error(string message, Exception? ex = null)
    {
        if (ex != null)
            ConsoleManager.WriteLog(() => Logger.Error(ex, message));
        else
            ConsoleManager.WriteLog(() => Logger.Error(message));
    }
    public static void Debug(string message) => ConsoleManager.WriteLog(() => Logger.Debug(message));
}

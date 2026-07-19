using Serilog;

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
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }

    public static void Info(string message) => _logger.Information(message);
    public static void Warn(string message) => _logger.Warning(message);
    public static void Error(string message, Exception? ex = null)
    {
        if (ex != null)
            _logger.Error(ex, message);
        else
            _logger.Error(message);
    }
    public static void Debug(string message) => _logger.Debug(message);
}

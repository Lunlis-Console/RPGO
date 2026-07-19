using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;

namespace RPGGame.ClientMonoGame;

public enum LogLevel
{
    Debug = 0, Info = 1, Warn = 2, Error = 3, Action = 4
}

public static class Logger
{
    public static LogLevel MinLevel { get; set; } = LogLevel.Debug;
    public static bool EchoToConsole { get; set; } = true;

    private static readonly string _logDir = Path.Combine(AppContext.BaseDirectory, "logs");
    private static readonly string _sessionFile = Path.Combine(
        _logDir, $"client_mono_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log");

    private static readonly BlockingCollection<string> _queue = new(new ConcurrentQueue<string>(), 4096);
    private static readonly Thread _writer;

    static Logger()
    {
        try { Directory.CreateDirectory(_logDir); } catch { }
        _writer = new Thread(WriterLoop) { IsBackground = true, Name = "Logger" };
        _writer.Start();
    }

    public static void Log(LogLevel level, string message)
    {
        if (level < MinLevel) return;
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] [{level}] {message}{Environment.NewLine}";
        if (!_queue.TryAdd(line))
            Console.Error.WriteLine("Logger queue full, dropping: " + line);
        if (EchoToConsole && level != LogLevel.Action)
        {
            var cc = level == LogLevel.Error ? ConsoleColor.Red
                   : level == LogLevel.Warn ? ConsoleColor.Yellow
                   : level == LogLevel.Info ? ConsoleColor.Gray
                   : ConsoleColor.DarkGray;
            var prev = Console.ForegroundColor;
            Console.ForegroundColor = cc;
            Console.Write(line);
            Console.ForegroundColor = prev;
        }
    }

    public static void Debug(string message) => Log(LogLevel.Debug, message);
    public static void Info(string message) => Log(LogLevel.Info, message);
    public static void Warn(string message) => Log(LogLevel.Warn, message);
    public static void Error(string message) => Log(LogLevel.Error, message);
    public static void Error(string message, Exception ex) =>
        Log(LogLevel.Error, message + " | " + ex.GetType().Name + ": " + ex.Message + Environment.NewLine + ex.StackTrace);
    public static void Action(string message) => Log(LogLevel.Action, "[ДЕЙСТВИЕ] " + message);

    private static void WriterLoop()
    {
        try
        {
            using var fs = new FileStream(_sessionFile, FileMode.Create, FileAccess.Write, FileShare.Read);
            using var sw = new StreamWriter(fs, new UTF8Encoding(false)) { AutoFlush = true };
            sw.WriteLine($"=== RPGO ClientMonoGame session log ===");
            sw.WriteLine($"Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sw.WriteLine();
            foreach (var line in _queue.GetConsumingEnumerable())
                sw.Write(line);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Logger writer failed: " + ex);
        }
    }
}

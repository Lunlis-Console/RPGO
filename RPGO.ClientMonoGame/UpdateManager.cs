using RPGGame.Shared.Models;
using RPGGame.Shared.Network;
using System.Diagnostics;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;

namespace RPGGame.ClientMonoGame;

/// <summary>
/// Стартовая проверка обновлений: сравнивает версию с сервером, скачивает
/// изменившиеся файлы (дельта по SHA256) и перезапускает клиент.
/// </summary>
public static class UpdateManager
{
    private const int Port = 7777;

    public static string BaseDir => AppDomain.CurrentDomain.BaseDirectory;

    /// <summary>Локальная версия клиента из version.json (пусто, если файла нет).</summary>
    public static string LocalVersion => ReadLocalVersion();
    private static string VersionFile => Path.Combine(BaseDir, "version.json");
    private static string UpdateDir => Path.Combine(BaseDir, "update");
    private static string StagingDir => Path.Combine(UpdateDir, "staging");

    /// <summary>
    /// Проверяет обновления на старте. Возвращает true, если нужно перезапуститься
    /// (обновление применено в staging).
    /// </summary>
    public static bool RunStartupCheck()
    {
        try
        {
            string ip = SettingsManager.Load().ServerIp;
            if (string.IsNullOrWhiteSpace(ip))
                return false;

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            return CheckAndApplyAsync(ip, cts.Token).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Logger.Warn($"[Update] Стартовая проверка пропущена: {ex.Message}");
            return false;
        }
    }

    /// <summary>Запускает скрытый apply.cmd и немедленно завершает текущий процесс.</summary>
    public static void RestartToApply()
    {
        try
        {
            string cmdPath = Path.Combine(UpdateDir, "apply.cmd");
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"\"{cmdPath}\"\"",
                WorkingDirectory = BaseDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            Logger.Error("[Update] Не удалось запустить apply.cmd", ex);
        }
    }

    private static async Task<bool> CheckAndApplyAsync(string ip, CancellationToken ct)
    {
        using var client = new TcpClient { NoDelay = true };
        await client.ConnectAsync(ip, Port, ct);
        var stream = client.GetStream();

        string localVersion = ReadLocalVersion();
        await Send(stream, new GameMessage { Type = "update_check", Data = new UpdateCheckRequest { Version = localVersion } });

        var infoMsg = await Receive(stream, ct);
        var info = Deserialize<UpdateInfo>(infoMsg?.Data);
        if (info == null || info.Files == null || info.Files.Count == 0)
            return false;

        if (string.Equals(info.Version, localVersion, StringComparison.OrdinalIgnoreCase))
            return false;

        Logger.Info($"[Update] Найдено обновление: v{localVersion} -> v{info.Version}");

        var toFetch = new List<UpdateFileEntry>();
        foreach (var entry in info.Files)
        {
            if (string.IsNullOrEmpty(entry.Path)) continue;
            string localPath = Path.Combine(BaseDir, entry.Path);
            if (!File.Exists(localPath) || !HashMatches(localPath, entry.Sha256))
                toFetch.Add(entry);
        }

        if (toFetch.Count == 0)
        {
            Logger.Info($"[Update] Файлы идентичны — просто обновляю версию до {info.Version}");
            WriteVersionFile(info.Version);
            return false;
        }

        Logger.Info($"[Update] Скачиваю {toFetch.Count} файлов...");
        Directory.CreateDirectory(StagingDir);
        foreach (var entry in toFetch)
            await DownloadFileAsync(stream, entry, ct);

        WriteVersionFile(Path.Combine(UpdateDir, "version.json"), info.Version);
        WriteApplyScript();
        return true;
    }

    private static async Task DownloadFileAsync(NetworkStream stream, UpdateFileEntry entry, CancellationToken ct)
    {
        await Send(stream, new GameMessage { Type = "update_file", Data = new UpdateFileRequest { Path = entry.Path } });

        string target = Path.Combine(StagingDir, entry.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        using var sha = SHA256.Create();
        using var fs = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None);
        long written = 0;

        while (true)
        {
            var msg = await Receive(stream, ct);
            if (msg == null)
                throw new IOException($"[Update] Нет ответа сервера для {entry.Path}");

            if (msg.Type == "update_file_missing")
                throw new IOException($"[Update] Файл не найден на сервере: {entry.Path}");

            if (msg.Type != "update_file_chunk")
                continue;

            var chunk = Deserialize<UpdateFileChunk>(msg.Data);
            if (chunk == null || !string.Equals(chunk.Path, entry.Path, StringComparison.OrdinalIgnoreCase))
                continue;

            byte[] data = Convert.FromBase64String(chunk.Data);
            sha.TransformBlock(data, 0, data.Length, null, 0);
            await fs.WriteAsync(data.AsMemory(0, data.Length), ct);
            written += data.Length;

            if (chunk.Done)
                break;
        }

        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        fs.Flush();

        string hash = Convert.ToHexString(sha.Hash!).ToLowerInvariant();
        if (!string.Equals(hash, entry.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new IOException($"[Update] Контрольная сумма не совпала: {entry.Path}");
        if (written != entry.Size)
            throw new IOException($"[Update] Размер файла не совпал: {entry.Path} ({written}/{entry.Size})");

        Logger.Info($"[Update] Загружен: {entry.Path} ({written} байт)");
    }

    private static void WriteApplyScript()
    {
        string script = $"""
            @echo off
            set "DIR=%~dp0.."
            set "STAGE=%~dp0staging"
            set "PID={Environment.ProcessId}"
            :wait
            tasklist /FI "PID eq %PID%" 2>nul | findstr /C:"%PID%" >nul
            if not errorlevel 1 (
              timeout /t 1 /nobreak >nul
              goto wait
            )
            xcopy /E /Y /Q "%STAGE%\*" "%DIR%\" >nul
            if exist "%~dp0version.json" copy /Y "%~dp0version.json" "%DIR%\version.json" >nul
            rmdir /S /Q "%STAGE%"
            del /Q "%~dp0version.json" 2>nul
            start "" "%DIR%\RPGO.ClientMonoGame.exe"
            del /Q "%~f0" 2>nul
            exit
            """;
        File.WriteAllText(Path.Combine(UpdateDir, "apply.cmd"), script);
    }

    private static string ReadLocalVersion()
    {
        try
        {
            if (File.Exists(VersionFile))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(VersionFile));
                if (doc.RootElement.TryGetProperty("version", out var v))
                    return v.GetString() ?? "";
            }
        }
        catch { }
        return "";
    }

    private static void WriteVersionFile(string version)
        => WriteVersionFile(VersionFile, version);

    private static void WriteVersionFile(string path, string version)
        => File.WriteAllText(path, JsonSerializer.Serialize(new { version }));

    private static bool HashMatches(string path, string expectedSha)
    {
        try
        {
            using var fs = File.OpenRead(path);
            string hash = Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
            return string.Equals(hash, expectedSha, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static Task Send(NetworkStream stream, GameMessage msg)
        => NetworkHelper.SendAsync(stream, msg);

    private static Task<GameMessage?> Receive(NetworkStream stream, CancellationToken ct)
        => NetworkHelper.ReceiveAsync<GameMessage>(stream, ct);

    private static T? Deserialize<T>(object? data)
    {
        if (data is JsonElement el)
            return JsonSerializer.Deserialize<T>(el.GetRawText());
        if (data is T t)
            return t;
        return default;
    }
}

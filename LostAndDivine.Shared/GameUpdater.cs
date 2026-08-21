using System.Diagnostics;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;

namespace LostAndDivine.Shared;

/// <summary>
/// Переиспользуемый движок обновления клиента. Работает и из самой игры, и из лаунчера:
/// проверяет версию на сервере, скачивает изменившиеся файлы, проверяет подпись манифеста
/// и атомарно применяет обновление (через apply.cmd с перезапуском текущего процесса).
/// Каталог обновления — BaseDirectory вызывающего процесса (папка установки игры).
/// </summary>
public static class GameUpdater
{
    private const int Port = 7777;

    public static string BaseDir => AppDomain.CurrentDomain.BaseDirectory;
    public static string VersionFile => Path.Combine(BaseDir, "version.json");
    public static string UpdateDir => Path.Combine(BaseDir, "update");
    public static string StagingDir => Path.Combine(UpdateDir, "staging");

    /// <summary>Локальная версия клиента из version.json (пусто, если файла нет).</summary>
    public static string LocalVersion => ReadLocalVersion();

    /// <summary>Результат проверки/применения обновлений.</summary>
    public sealed class GameUpdateResult
    {
        public bool RestartRequired { get; init; }
        public string Message { get; init; } = "";
    }

    /// <summary>Хук логирования (по умолчанию — Console).</summary>
    public static Action<string> Log { get; set; } = m => Console.WriteLine(m);

    /// <summary>
    /// Проверяет обновления и применяет их при наличии. Возвращает флаг необходимости
    /// перезапуска (обновление уже записано в staging и готово к применению).
    /// </summary>
    public static async Task<GameUpdateResult> CheckAndApplyAsync(string ip, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var report = new Action<string>(m => { Log(m); progress?.Report(m); });

        if (string.IsNullOrWhiteSpace(ip))
            return new GameUpdateResult { Message = "Не указан IP сервера" };
        if (!File.Exists(VersionFile))
            return new GameUpdateResult { Message = "Dev-сборка: version.json отсутствует, авто-обновление отключено" };

        string localVersion = ReadLocalVersion();
        try
        {
            using var client = new TcpClient { NoDelay = true };
            await client.ConnectAsync(ip, Port, ct);
            var stream = client.GetStream();

            await Send(stream, new GameMessage { Type = "update_check", Data = new UpdateCheckRequest { Version = localVersion } });
            var infoMsg = await Receive<GameMessage>(stream, ct);
            var info = Deserialize<UpdateInfo>(infoMsg?.Data);
            if (info == null || info.Files == null || info.Files.Count == 0)
                return new GameUpdateResult { Message = "Сервер не отдал список обновлений (на сервере нет client_build)" };

            // Опубликованный клиент: манифест обязан быть подписан и валиден.
            // Без валидной подписи обновление отклоняется (защита от подмены/ MITM).
            if (string.IsNullOrWhiteSpace(info.Signature) || !UpdateSigner.Verify(info, SigningKeys.PublicKeyPem))
                return new GameUpdateResult { Message = "Обновление отклонено: подпись манифеста недействительна (возможно, подмена)." };

            // Защита от отката версии (downgrade-атаки).
            if (CompareVersions(info.Version, localVersion) < 0)
                return new GameUpdateResult { Message = $"Обновление отклонено: сервер предлагает более старую версию ({info.Version} < {localVersion})." };

            if (string.Equals(info.Version, localVersion, StringComparison.OrdinalIgnoreCase))
                return new GameUpdateResult { Message = $"Обновление не требуется — версия актуальна (v{localVersion})" };

            report($"Найдено обновление: v{localVersion} -> v{info.Version}");

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
                report("Файлы идентичны — просто обновляю версию");
                WriteVersionFile(Path.Combine(UpdateDir, "version.json"), info.Version);
                return new GameUpdateResult { RestartRequired = true, Message = $"Обновление до v{info.Version} готово — перезапуск..." };
            }

            report($"Скачиваю {toFetch.Count} файлов...");
            Directory.CreateDirectory(StagingDir);
            int done = 0;
            foreach (var entry in toFetch)
            {
                await DownloadFileAsync(stream, entry, report, ct);
                done++;
                progress?.Report($"Скачивание: {done}/{toFetch.Count}");
            }

            WriteVersionFile(Path.Combine(UpdateDir, "version.json"), info.Version);
            WriteApplyScript();
            return new GameUpdateResult { RestartRequired = true, Message = $"Обновление до v{info.Version} готово — перезапуск..." };
        }
        catch (OperationCanceledException)
        {
            return new GameUpdateResult { Message = "Ошибка: таймаут проверки обновлений" };
        }
        catch (Exception ex)
        {
            report($"Ошибка проверки: {ex.Message}");
            return new GameUpdateResult { Message = $"Ошибка: {ex.Message}" };
        }
    }

    /// <summary>Запрашивает у сервера историю изменений (changelog) без входа в игру.</summary>
    public static async Task<ChangelogData?> FetchChangelogAsync(string ip, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ip)) return null;
        try
        {
            using var client = new TcpClient { NoDelay = true };
            await client.ConnectAsync(ip, Port, ct);
            var stream = client.GetStream();
            await Send(stream, new GameMessage { Type = "changelog", Data = new { } });
            var msg = await Receive<GameMessage>(stream, ct);
            return Deserialize<ChangelogData>(msg?.Data);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Запускает apply.cmd и немедленно завершает текущий процесс (для применения обновления).</summary>
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
            Log($"[Update] Не удалось запустить apply.cmd: {ex.Message}");
        }
    }

    private static async Task DownloadFileAsync(NetworkStream stream, UpdateFileEntry entry, Action<string> report, CancellationToken ct)
    {
        await Send(stream, new GameMessage { Type = "update_file", Data = new UpdateFileRequest { Path = entry.Path } });

        string target = Path.Combine(StagingDir, entry.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        using var sha = SHA256.Create();
        using var fs = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None);
        long written = 0;

        while (true)
        {
            var msg = await Receive<GameMessage>(stream, ct);
            if (msg == null)
                throw new IOException($"Нет ответа сервера для {entry.Path}");
            if (msg.Type == "update_file_missing")
                throw new IOException($"Файл не найден на сервере: {entry.Path}");
            if (msg.Type != "update_file_chunk")
                continue;

            var chunk = Deserialize<UpdateFileChunk>(msg.Data);
            if (chunk == null || !string.Equals(chunk.Path, entry.Path, StringComparison.OrdinalIgnoreCase))
                continue;

            byte[] data = Convert.FromBase64String(chunk.Data);
            sha.TransformBlock(data, 0, data.Length, null, 0);
            await fs.WriteAsync(data.AsMemory(0, data.Length), ct);
            written += data.Length;
            if (chunk.Done) break;
        }

        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        fs.Flush();

        string hash = Convert.ToHexString(sha.Hash!).ToLowerInvariant();
        if (!string.Equals(hash, entry.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new IOException($"Контрольная сумма не совпала: {entry.Path}");
        if (written != entry.Size)
            throw new IOException($"Размер файла не совпал: {entry.Path} ({written}/{entry.Size})");

        report($"Загружен: {entry.Path} ({written} байт)");
    }

    private static void WriteApplyScript()
    {
        string self = Process.GetCurrentProcess().MainModule?.FileName ?? "";
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
            {(!string.IsNullOrEmpty(self) ? $"start \"\" \"{self}\"" : "")}
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
        catch { return false; }
    }

    /// <summary>Сравнивает версии вида a.b.c.d. Возвращает &lt;0, 0 или &gt;0.</summary>
    public static int CompareVersions(string a, string b)
    {
        var pa = (a ?? "").Split('.');
        var pb = (b ?? "").Split('.');
        int n = Math.Max(pa.Length, pb.Length);
        for (int i = 0; i < n; i++)
        {
            int xa = i < pa.Length && int.TryParse(pa[i], out var va) ? va : 0;
            int xb = i < pb.Length && int.TryParse(pb[i], out var vb) ? vb : 0;
            if (xa != xb) return xa.CompareTo(xb);
        }
        return 0;
    }

    private static Task Send(NetworkStream stream, GameMessage msg)
        => NetworkHelper.SendAsync(stream, msg);

    private static Task<T?> Receive<T>(NetworkStream stream, CancellationToken ct)
        => NetworkHelper.ReceiveAsync<T>(stream, ct);

    private static T? Deserialize<T>(object? data)
    {
        if (data is JsonElement el)
            return JsonSerializer.Deserialize<T>(el.GetRawText());
        if (data is T t)
            return t;
        return default;
    }
}

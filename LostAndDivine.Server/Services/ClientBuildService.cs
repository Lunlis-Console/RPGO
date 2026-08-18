using LostAndDivine.Server.Network;
using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;
using System.Security.Cryptography;
using System.Text.Json;

namespace LostAndDivine.Server.Services;

/// <summary>
/// Раздача обновлений клиента. Манифест читается из client_build/manifest.json
/// (генерируется build-client-build.ps1). Файлы отдаются чанками по update_file_chunk.
/// </summary>
public class ClientBuildService
{
    private const int ChunkSize = 384 * 1024;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private string _filesDir = "";

    public UpdateInfo? Info { get; private set; }

    /// <summary>Список изменений «Что нового» из client_build/changelog.json.</summary>
    public ChangelogData? Changelog { get; private set; }

    public void Initialize()
    {
        string baseDir = AppContext.BaseDirectory;
        string? dir = FindClientBuildDir(baseDir);
        if (dir == null)
        {
            Log.Info("client_build/manifest.json не найден — обновления клиента не настроены");
            return;
        }

        _filesDir = Path.Combine(dir, "files");
        try
        {
            string json = File.ReadAllText(Path.Combine(dir, "manifest.json"));
            Info = JsonSerializer.Deserialize<UpdateInfo>(json, JsonOpts);
            Log.Info($"Манифест клиента загружен: v{Info?.Version} ({Info?.Files.Count ?? 0} файлов)");
        }
        catch (Exception ex)
        {
            Log.Error("Ошибка чтения client_build/manifest.json", ex);
            Info = null;
        }

        try
        {
            string clPath = Path.Combine(dir, "changelog.json");
            if (File.Exists(clPath))
            {
                var cl = JsonSerializer.Deserialize<ChangelogData>(File.ReadAllText(clPath), JsonOpts);
                if (cl?.Entries != null && cl.Entries.Count > 0)
                {
                    Changelog = cl;
                    Log.Info($"Changelog загружен: {cl.Entries.Count} записей");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Ошибка чтения client_build/changelog.json", ex);
            Changelog = null;
        }
    }

    private static string? FindClientBuildDir(string startDir)
    {
        var dir = Path.GetFullPath(startDir);
        for (int i = 0; i < 7; i++)
        {
            if (File.Exists(Path.Combine(dir, "client_build", "manifest.json")))
                return Path.Combine(dir, "client_build");
            var parent = Path.GetDirectoryName(dir);
            if (parent == null) break;
            dir = parent;
        }
        return null;
    }

    /// <summary>Обрабатывает update_check/update_file до авторизации. Возвращает true, если сообщение обработано.</summary>
    public async Task<bool> HandleUnauthenticatedAsync(ClientConnection connection, GameMessage message, INetworkHub hub)
    {
        switch (message.Type)
        {
            case "update_check":
            {
                var check = Deserialize<UpdateCheckRequest>(message.Data);
                await hub.SendToClient(connection, new GameMessage
                {
                    Type = "update_info",
                    Data = Info ?? new UpdateInfo()
                });
                Log.Info($"Клиент проверил версию: локальная '{check?.Version}', серверная '{Info?.Version}'");
                return true;
            }

            case "update_file":
            {
                var req = Deserialize<UpdateFileRequest>(message.Data);
                if (req == null || string.IsNullOrEmpty(req.Path))
                    return true;

                string? full = ResolvePath(req.Path);
                if (full == null)
                {
                    await hub.SendToClient(connection, new GameMessage
                    {
                        Type = "update_file_missing",
                        Data = new { Path = req.Path }
                    });
                    return true;
                }

                var fileInfo = new FileInfo(full);
                long total = fileInfo.Length;
                var buffer = new byte[ChunkSize];

                // Стриминг вместо File.ReadAllBytes: файлы клиента достигают сотен МБ,
                // и загрузка целиком в память убивает процесс на маленьких VPS (OOM killer).
                string sha;
                using (var shaAlg = SHA256.Create())
                using (var fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    int read;
                    while ((read = await fs.ReadAsync(buffer.AsMemory(0, ChunkSize))) > 0)
                        shaAlg.TransformBlock(buffer, 0, read, null, 0);
                    shaAlg.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                    sha = Convert.ToHexString(shaAlg.Hash!);
                }

                using (var fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    long offset = 0;
                    while (offset < total)
                    {
                        int len = (int)Math.Min(ChunkSize, total - offset);
                        int read = await fs.ReadAsync(buffer.AsMemory(0, len));
                        if (read == 0) break;
                        await hub.SendToClient(connection, new GameMessage
                        {
                            Type = "update_file_chunk",
                            Data = new UpdateFileChunk
                            {
                                Path = req.Path,
                                Offset = offset,
                                Data = Convert.ToBase64String(buffer, 0, read),
                                TotalLength = total,
                                Sha256 = sha,
                                Done = offset + read >= total
                            }
                        });
                        offset += read;
                    }
                }
                Log.Info($"Отдан файл обновления: {req.Path} ({total} байт)");
                return true;
            }

            case "ping":
            {
                var ping = Deserialize<PingMessage>(message.Data);
                await hub.SendToClient(connection, new GameMessage
                {
                    Type = "pong",
                    Data = new PongMessage(ping?.Seq ?? 0, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
                });
                return true;
            }
        }

        return false;
    }

    /// <summary>Отправляет игроку список изменений «Что нового» (после welcome).</summary>
    public async Task SendChangelogAsync(ClientConnection connection, INetworkHub hub)
    {
        if (Changelog == null) return;
        await hub.SendToClient(connection, new GameMessage
        {
            Type = "changelog",
            Data = new ChangelogData
            {
                Version = Info?.Version ?? Changelog.Version,
                Entries = Changelog.Entries
            }
        });
    }

    /// <summary>Проверяет путь в пределах _filesDir (без выхода за границы) и возвращает полный путь к файлу.</summary>
    private string? ResolvePath(string relPath)
    {
        if (Info == null || _filesDir.Length == 0) return null;

        string root = Path.GetFullPath(_filesDir);
        string full = Path.GetFullPath(Path.Combine(root, relPath));
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return null;
        return File.Exists(full) ? full : null;
    }

    private static T? Deserialize<T>(object? data)
    {
        if (data == null) return default;
        return JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(data), JsonOpts);
    }
}

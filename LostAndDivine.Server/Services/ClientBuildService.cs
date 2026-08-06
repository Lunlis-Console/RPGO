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

                if (!TryGetFile(req.Path, out var bytes))
                {
                    await hub.SendToClient(connection, new GameMessage
                    {
                        Type = "update_file_missing",
                        Data = new { Path = req.Path }
                    });
                    return true;
                }

                string sha = Convert.ToHexString(SHA256.HashData(bytes));
                long total = bytes.Length;
                for (int offset = 0; offset < total; offset += ChunkSize)
                {
                    int len = (int)Math.Min(ChunkSize, total - offset);
                    await hub.SendToClient(connection, new GameMessage
                    {
                        Type = "update_file_chunk",
                        Data = new UpdateFileChunk
                        {
                            Path = req.Path,
                            Offset = offset,
                            Data = Convert.ToBase64String(bytes, offset, len),
                            TotalLength = total,
                            Sha256 = sha,
                            Done = offset + len >= total
                        }
                    });
                }
                Log.Info($"Отдан файл обновления: {req.Path} ({total} байт)");
                return true;
            }
        }

        return false;
    }

    private bool TryGetFile(string relPath, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (Info == null || _filesDir.Length == 0) return false;

        string root = Path.GetFullPath(_filesDir);
        string full = Path.GetFullPath(Path.Combine(root, relPath));
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!File.Exists(full)) return false;

        bytes = File.ReadAllBytes(full);
        return true;
    }

    private static T? Deserialize<T>(object? data)
    {
        if (data == null) return default;
        return JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(data), JsonOpts);
    }
}

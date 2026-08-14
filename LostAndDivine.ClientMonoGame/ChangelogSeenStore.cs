using System.Text.Json;

namespace LostAndDivine.ClientMonoGame;

/// <summary>
/// Хранит версию обновления, которую игрок уже видел в окне «Что нового».
/// Файл changelog_seen.json рядом с клиентом (по образцу version.json).
/// </summary>
public static class ChangelogSeenStore
{
    private static string FilePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "changelog_seen.json");

    private static string? _cachedSeen;

    private static string ReadSeen()
    {
        if (_cachedSeen != null) return _cachedSeen;
        try
        {
            if (File.Exists(FilePath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(FilePath));
                if (doc.RootElement.TryGetProperty("version", out var v))
                    _cachedSeen = v.GetString() ?? "";
            }
        }
        catch
        {
            _cachedSeen = "";
        }
        return _cachedSeen ?? "";
    }

    public static void WriteSeen(string version)
    {
        _cachedSeen = version;
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(new { version }));
        }
        catch
        {
            // Не критично: при следующем входе покажем окно ещё раз
        }
    }

    /// <summary>
    /// Новее ли версия сервера, чем та, что игрок уже видел.
    /// Версии вида "0.1.299"; если распарсить не удаётся — считаем новой.
    /// </summary>
    public static bool IsNewer(string serverVersion)
    {
        if (string.IsNullOrWhiteSpace(serverVersion)) return false;
        if (!TryParsePatch(serverVersion, out int serverPatch)) return true;

        string seen = ReadSeen();
        if (string.IsNullOrWhiteSpace(seen)) return true;
        if (!TryParsePatch(seen, out int seenPatch)) return true;

        return serverPatch > seenPatch;
    }

    private static bool TryParsePatch(string version, out int patch)
    {
        patch = 0;
        var parts = version.Split('.');
        return parts.Length >= 3 && int.TryParse(parts[^1], out patch);
    }
}
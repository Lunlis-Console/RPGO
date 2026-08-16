using Microsoft.Data.Sqlite;

namespace LostAndDivine.Shared;

/// <summary>
/// Одноразовая миграция контента из runtime-БД (game.db) в content.db.
/// Вызывается только когда content.db был создан заново (первый запуск после разделения).
/// </summary>
public static class ContentDbSeeder
{
    /// <summary>Таблицы контента: живут в content.db, коммитятся в git.</summary>
    public static readonly string[] Tables =
    {
        "quests_def", "npcs", "merchant_stock", "items", "monsters", "skills",
        "monster_drops", "world_config", "world_portals", "zones", "tile_maps", "tilesets",
        "instance_templates", "instance_portals", "instance_spawns"
    };

    /// <summary>
    /// Если runtime-БД (game.db) содержит контент, а content.db только что создан (пуст от контента),
    /// копирует таблицы контента из runtime в content.
    /// </summary>
    public static void CopyContentFromRuntimeIfNew(string contentConnectionString, string runtimeDbPath)
    {
        if (string.IsNullOrWhiteSpace(runtimeDbPath) || !File.Exists(runtimeDbPath)) return;

        using var content = new SqliteConnection(contentConnectionString);
        content.Open();

        bool contentEmpty;
        using (var cmd = content.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM npcs";
            contentEmpty = (long)(cmd.ExecuteScalar() ?? 0) == 0;
        }
        if (!contentEmpty) return;

        using var runtime = new SqliteConnection($"Data Source={runtimeDbPath}");
        runtime.Open();
        using (var check = runtime.CreateCommand())
        {
            check.CommandText = "SELECT COUNT(*) FROM npcs";
            if ((long)(check.ExecuteScalar() ?? 0) == 0) return;
        }

        using (var attach = content.CreateCommand())
            attach.CommandText = $"ATTACH DATABASE '{runtimeDbPath.Replace("\\", "/").Replace("'", "''")}' AS runtime";

        foreach (var table in Tables)
        {
            using var del = content.CreateCommand();
            del.CommandText = $"DELETE FROM main.{table}";
            del.ExecuteNonQuery();
            using var cmd = content.CreateCommand();
            cmd.CommandText = $"INSERT INTO main.{table} SELECT * FROM runtime.{table}";
            cmd.ExecuteNonQuery();
        }

        try
        {
            using var detach = content.CreateCommand();
            detach.CommandText = "DETACH DATABASE runtime";
            detach.ExecuteNonQuery();
        }
        catch { /* детач в конце подключения не критичен */ }
    }
}

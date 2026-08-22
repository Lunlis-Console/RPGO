using System.Data;
using System.IO;
using System.Linq;
using System.Text.Json;
using LostAndDivine.Shared;
using LostAndDivine.Shared.Migrations;
using LostAndDivine.Shared.Models;
using Microsoft.Data.Sqlite;

namespace LostAndDivine.Editor;

/// <summary>
/// Доступ к базе (game.db и content.db), миграции, справочники и пути контента клиента.
/// Все SQL-запросы — порт из WinForms-версии редактора.
/// </summary>
    public sealed class Db
    {
        public string DbFile { get; }
        /// <summary>Живой content.db, который читает работающий сервер (P0-3: НЕ трогаем напрямую).</summary>
        public string LiveContentDbFile { get; }
        /// <summary>Staging-копия content.editor.db, с которой работает редактор (Вариант А).</summary>
        public string ContentDbFile { get; }

    public List<(string Id, string Name)> MonsterRefs { get; private set; } = new();
    public List<(string Id, string Name)> CollectibleRefs { get; private set; } = new();
    public List<(string Id, string Name)> NpcRefs { get; private set; } = new();
    public List<(string Id, string Name)> QuestRefs { get; private set; } = new();
    public List<(string Id, string Name)> RewardItemRefs { get; private set; } = new();

    private Dictionary<string, string> _npcNameById = new();
    private Dictionary<string, string> _npcLocationByName = new();
    private Dictionary<(int, int), string> _npcPosToName = new();
    private Dictionary<string, string> _zoneNames = new();
    private Dictionary<string, string> _npcZoneByName = new(StringComparer.OrdinalIgnoreCase);

    public static readonly JsonSerializerOptions QuestJsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public Db(string dbFile)
    {
        DbFile = dbFile;
        string dir = Path.GetDirectoryName(dbFile) ?? ".";
        LiveContentDbFile = Path.Combine(dir, "content.db");
        // Редактор работает с отдельной staging-копией, чтобы случайный/битый
        // save не бил по живому content.db сервера (P0-3, Вариант А).
        ContentDbFile = Path.Combine(dir, "content.editor.db");
    }

    public void InitAndLoadAll()
    {
        DbMigrationRunner.RunMigrations($"Data Source={DbFile}");

        // Staging-копия создаётся из живого content.db при первом запуске редактора.
        EnsureStagingExists();

        string contentConn = $"Data Source={ContentDbFile}";
        DbMigrationRunner.RunMigrations(contentConn);

        LoadMonsterRefs();
        LoadCollectibleRefs();
        LoadNpcRefs();
        LoadZoneNames();
        BuildNpcZoneMapFromTiled();
        LoadQuestRefs();
        LoadRewardItemRefs();
    }

    /// <summary>
    /// Гарантирует наличие staging-копии content.editor.db. Если её нет, копирует
    /// текущий живой content.db (если он есть) — редактор начинает с актуального контента.
    /// </summary>
    private void EnsureStagingExists()
    {
        if (File.Exists(ContentDbFile)) return;
        if (File.Exists(LiveContentDbFile))
            File.Copy(LiveContentDbFile, ContentDbFile);
    }

    /// <summary>
    /// Публикация: атомарно переносит staging-контент (content.editor.db) в живой
    /// content.db. Делается через SQL (ATTACH + копия таблиц в транзакции), что
    /// устойчиво к удерживаемым сервером файловым блокировкам. Перед переносом
    /// живой content.db бэкапится (VACUUM INTO). Редактор не пишет в live напрямую.
    /// </summary>
    public void PublishToLive()
    {
        if (!File.Exists(ContentDbFile))
            throw new InvalidOperationException("Staging content.editor.db не найден — нечего публиковать.");

        string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        string backupPath = LiveContentDbFile + ".publishbak_" + stamp;

        using var live = new SqliteConnection($"Data Source={LiveContentDbFile}");
        live.Open();
        using var staging = new SqliteConnection($"Data Source={ContentDbFile}");
        staging.Open();

        // Бэкап живого content.db перед перезаписью
        using (var bk = live.CreateCommand())
        {
            bk.CommandText = $"VACUUM INTO '{backupPath.Replace("'", "''")}'";
            bkSafe(bk);
        }

        // Подключаем staging как вторую БД и копируем таблицы в транзакции
        using (var attach = live.CreateCommand())
        {
            attach.CommandText = $"ATTACH DATABASE '{ContentDbFile.Replace("\\", "/").Replace("'", "''")}' AS staging";
            attach.ExecuteNonQuery();
        }

        using var tx = live.BeginTransaction();
        foreach (var table in ContentDbSeeder.Tables)
        {
            // Пропускаем таблицы, которых нет в staging
            bool inStaging;
            using (var c = staging.CreateCommand())
            {
                c.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name=$t";
                c.Parameters.AddWithValue("$t", table);
                inStaging = c.ExecuteScalar() != null;
            }
            if (!inStaging) continue;

            using var del = live.CreateCommand();
            del.CommandText = $"DELETE FROM main.{table}";
            del.ExecuteNonQuery();
            using var ins = live.CreateCommand();
            ins.CommandText = $"INSERT INTO main.{table} SELECT * FROM staging.{table}";
            ins.ExecuteNonQuery();
        }
        tx.Commit();

        Console.WriteLine($"[Editor] Контент опубликован в {LiveContentDbFile} (бэкап: {backupPath})");
    }

    private static void bkSafe(SqliteCommand bk)
    {
        try { bk.ExecuteNonQuery(); }
        catch (Exception ex) { Console.WriteLine($"[Editor] WARNING: бэкап live content.db не удался: {ex.Message}"); }
    }

    // === справочники ===

    public void LoadMonsterRefs() => MonsterRefs = LoadRefs("SELECT id, name FROM monsters ORDER BY id");
    public void LoadCollectibleRefs() => CollectibleRefs = LoadRefs("SELECT id, name FROM items WHERE type='collectible' ORDER BY id");
    public void LoadQuestRefs() => QuestRefs = LoadRefs("SELECT id, title FROM quests_def ORDER BY id");
    public void LoadRewardItemRefs() => RewardItemRefs = LoadRefs("SELECT id, name FROM items WHERE type <> 'collectible' ORDER BY id");

    public void LoadNpcRefs()
    {
        NpcRefs = new List<(string, string)>();
        _npcLocationByName = new Dictionary<string, string>();
        _npcNameById = new Dictionary<string, string>();
        _npcPosToName = new Dictionary<(int, int), string>();
        using var conn = OpenContent();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, location, x, y FROM npcs ORDER BY id";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            string name = reader.GetString(1);
            string loc = reader.IsDBNull(2) ? "" : reader.GetString(2);
            int x = reader.IsDBNull(3) ? 0 : reader.GetInt32(3);
            int y = reader.IsDBNull(4) ? 0 : reader.GetInt32(4);
            NpcRefs.Add((reader.GetString(0), name));
            _npcNameById[reader.GetString(0)] = name;
            _npcLocationByName[name] = loc;
            _npcPosToName[(x, y)] = name;
        }
    }

    private void LoadZoneNames()
    {
        _zoneNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var conn = OpenContent();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, name FROM zones ORDER BY id";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                _zoneNames[reader.GetString(0)] = reader.IsDBNull(1) ? reader.GetString(0) : reader.GetString(1);
        }
        catch { _zoneNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); }
    }

    /// <summary>Локация NPC по id (зона размещения на Tiled-картах, приоритетнее ручного поля).</summary>
    public string NpcLocationById(string npcId)
    {
        if (string.IsNullOrEmpty(npcId)) return "";
        if (_npcNameById.TryGetValue(npcId, out var name) && _npcLocationByName.TryGetValue(name, out var loc)) return loc;
        return "";
    }

    public string ZoneDisplayName(string zoneId)
        => _zoneNames.TryGetValue(zoneId, out var zn) ? zn : zoneId;

    /// <summary>Сопоставляет NPC с зоной, в которой он размещён на Tiled-картах.</summary>
    public void BuildNpcZoneMapFromTiled()
    {
        _npcZoneByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var npcTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "npc", "merchant", "board", "instance_portal", "dummy"
        };

        foreach (var file in FindTiledZoneMaps())
        {
            string zoneId = Path.GetFileNameWithoutExtension(file);
            if (zoneId.StartsWith("zone_", StringComparison.OrdinalIgnoreCase))
                zoneId = zoneId["zone_".Length..];
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                int tileW = 64, tileH = 64;
                if (doc.RootElement.TryGetProperty("tilewidth", out var tw) && tw.ValueKind == JsonValueKind.Number) tileW = tw.GetInt32();
                if (doc.RootElement.TryGetProperty("tileheight", out var th) && th.ValueKind == JsonValueKind.Number) tileH = th.GetInt32();
                if (!doc.RootElement.TryGetProperty("layers", out var layers)) continue;
                foreach (var layer in layers.EnumerateArray())
                {
                    if (!layer.TryGetProperty("type", out var t) || !string.Equals(t.GetString(), "objectgroup", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!layer.TryGetProperty("objects", out var objs)) continue;
                    foreach (var o in objs.EnumerateArray())
                    {
                        string type = o.TryGetProperty("type", out var ot) ? (ot.GetString() ?? "") : "";
                        if (!npcTypes.Contains(type)) continue;
                        string tiledName = o.TryGetProperty("name", out var on) ? (on.GetString() ?? "") : "";
                        double ox = o.TryGetProperty("x", out var oxp) && oxp.ValueKind == JsonValueKind.Number ? oxp.GetDouble() : 0;
                        double oy = o.TryGetProperty("y", out var oyp) && oyp.ValueKind == JsonValueKind.Number ? oyp.GetDouble() : 0;
                        int tx = (int)(ox / tileW);
                        int ty = (int)(oy / tileH);
                        string? npcName = null;
                        if (!string.IsNullOrWhiteSpace(tiledName) && _npcNameById.TryGetValue(tiledName, out var byId)) npcName = byId;
                        else if (!string.IsNullOrWhiteSpace(tiledName) && _npcLocationByName.ContainsKey(tiledName)) npcName = tiledName;
                        else if (_npcPosToName.TryGetValue((tx, ty), out var byPos)) npcName = byPos;
                        if (npcName == null) continue;
                        _npcZoneByName[npcName] = zoneId;
                    }
                }
            }
            catch { /* игнорируем битые карты */ }
        }

        foreach (var kvp in _npcZoneByName)
        {
            string loc = _zoneNames.TryGetValue(kvp.Value, out var zn) ? zn : kvp.Value;
            _npcLocationByName[kvp.Key] = loc;
        }
    }

    /// <summary>Зона размещения NPC по имени (из Tiled).</summary>
    public string NpcZoneByName(string name)
        => _npcZoneByName.TryGetValue(name, out var z) ? z : "";

    public string LocationByName(string npcName)
        => _npcLocationByName.TryGetValue(npcName, out var l) ? l : "";

    public IEnumerable<string> FindTiledZoneMaps()
    {
        var root = FindSolutionRoot(Path.GetDirectoryName(ContentDbFile) ?? ".");
        var found = new List<string>();
        if (!string.IsNullOrEmpty(root))
            ScanTiledMaps(new DirectoryInfo(root), found, 0, 6);
        return found;
    }

    public static string? FindSolutionRoot(string startDir)
    {
        var dir = startDir;
        while (!string.IsNullOrEmpty(dir))
        {
            if (Directory.Exists(Path.Combine(dir, ".git")) ||
                Directory.GetFiles(dir, "*.sln").Length > 0)
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return startDir;
    }

    private static void ScanTiledMaps(DirectoryInfo dir, List<string> found, int depth, int maxDepth)
    {
        if (depth > maxDepth) return;
        if (depth > 0)
        {
            var lower = dir.Name.ToLowerInvariant();
            if (lower is "bin" or "obj" or "node_modules" or ".git" or "dist") return;
        }
        try
        {
            foreach (var f in dir.GetFiles("zone_*.tmj"))
                found.Add(f.FullName);
            foreach (var sub in dir.GetDirectories())
                ScanTiledMaps(sub, found, depth + 1, maxDepth);
        }
        catch { }
    }

    // === подключения и таблицы ===

    public SqliteConnection OpenGame() => Open(DbFile);
    public SqliteConnection OpenContent() => Open(ContentDbFile);

    private static SqliteConnection Open(string file)
    {
        var conn = new SqliteConnection($"Data Source={file}");
        conn.Open();
        return conn;
    }

    public List<(string Id, string Name)> LoadRefs(string query)
    {
        var list = new List<(string, string)>();
        using var conn = OpenContent();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = query;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add((reader.GetString(0), reader.GetString(1)));
        return list;
    }

    /// <summary>Читает таблицу целиком строками (как в WinForms-версии: всё приводится к string).</summary>
    public DataTable LoadTable(string query)
    {
        var dt = new DataTable();
        using var conn = OpenContent();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = query;
        using var reader = cmd.ExecuteReader();
        for (int i = 0; i < reader.FieldCount; i++)
            dt.Columns.Add(reader.GetName(i), typeof(string));
        while (reader.Read())
        {
            var values = new object[reader.FieldCount];
            for (int i = 0; i < reader.FieldCount; i++)
                values[i] = reader.IsDBNull(i) ? "" : string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}", reader.GetValue(i));
            dt.Rows.Add(values);
        }
        return dt;
    }

    // === пути контента клиента ===

    public string ClientBinContent()
    {
        var solRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        return Path.Combine(solRoot, "LostAndDivine.ClientMonoGame", "bin", "Debug", "net8.0", "Content");
    }

    public string ClientSrcContent()
    {
        var solRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        return Path.Combine(solRoot, "LostAndDivine.ClientMonoGame", "Content");
    }

    // === хелперы ===

    public static int ToInt(object? v) => int.TryParse(v?.ToString(), out int r) ? r : 0;
    public static double ToDouble(object? v) => double.TryParse(v?.ToString(), System.Globalization.CultureInfo.InvariantCulture, out double r) ? r : 0;
    public static int QuestFlag(object? v) => v is bool b ? (b ? 1 : 0) : ToInt(v);

    /// <summary>
    /// Удаляет из таблицы только строки, чей PK отсутствует в <paramref name="ids"/>.
    /// Безопасная замена "DELETE FROM table": не стирает весь столбец целиком и не ломает
    /// identity/FK для строк, которые редактор не трогал (аудит P1-8).
    /// </summary>
    public static void DeleteMissingRows(SqliteConnection conn, SqliteTransaction tx, string table, string pk, IEnumerable<string> ids)
    {
        var list = ids.ToList();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        if (list.Count == 0)
            cmd.CommandText = $"DELETE FROM {table}";
        else
        {
            var inList = string.Join(",", list.Select(x => "'" + x.Replace("'", "''") + "'"));
            cmd.CommandText = $"DELETE FROM {table} WHERE {pk} NOT IN ({inList})";
        }
        cmd.ExecuteNonQuery();
    }

    public static string NameById(List<(string Id, string Name)> refs, string id)
    {
        var found = refs.FirstOrDefault(r => r.Id == id);
        return found.Name ?? "";
    }

    public static string IdByName(List<(string Id, string Name)> refs, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        return refs.FirstOrDefault(r => r.Name == name).Id ?? "";
    }

    /// <summary>Следующий свободный ID с префиксом (I0007, M0012...).</summary>
    public static string NextId(DataTable dt, string prefix)
    {
        int maxNum = 0;
        foreach (DataRow row in dt.Rows)
        {
            if (row.RowState == DataRowState.Deleted) continue;
            var id = row["id"]?.ToString() ?? "";
            if (id.StartsWith(prefix) && int.TryParse(id[prefix.Length..], out int num))
                maxNum = Math.Max(maxNum, num);
        }
        return prefix + (maxNum + 1).ToString("D4");
    }

    /// <summary>Заполняет пустые id в таблице (как EnsureId в WinForms-версии).</summary>
    public static void EnsureId(DataTable dt, string prefix)
    {
        foreach (DataRow row in dt.Rows)
        {
            if (row.RowState == DataRowState.Deleted) continue;
            if (string.IsNullOrWhiteSpace(row["id"]?.ToString()))
                row["id"] = NextId(dt, prefix);
        }
    }

    public static string HashPassword(string password)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        byte[] bytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(password));
        return Convert.ToBase64String(bytes);
    }

    public static bool IsChecked(object? v)
    {
        if (v is bool b) return b;
        if (v is int i) return i != 0;
        if (v is string s) return s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase);
        return false;
    }
}
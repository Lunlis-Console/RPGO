using System.Data;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LostAndDivine.Shared.Data;

namespace LostAndDivine.Editor.Views;

public partial class NpcsTabView : UserControl
{
    private MainWindow _win = null!;
    private Db _db = null!;
    private DataTable _dt = new();

    public NpcsTabView()
    {
        InitializeComponent();
        AddBtn.Click += (s, e) => Ui.AddRowWithId(Grid, _dt, "N");
        DeleteBtn.Click += (s, e) => Ui.DeleteSelectedRow(Grid);
        EditBtn.Click += (s, e) => EditSelected();
        SaveBtn.Click += (s, e) => SaveWorld();
        Grid.MouseDoubleClick += (s, e) =>
        {
            if (e.OriginalSource is TextBlock or Border) EditSelected();
        };
    }

    public void Init(MainWindow win)
    {
        _win = win;
        _db = win.Db;
        WorldWidth.Text = GetWorldConfigInt("width", 100).ToString();
        WorldHeight.Text = GetWorldConfigInt("height", 100).ToString();
        LoadWorld();
    }

    private void LoadWorld()
    {
        _dt = new DataTable();
        _dt.Columns.Add("id", typeof(string));
        _dt.Columns.Add("name", typeof(string));
        _dt.Columns.Add("type", typeof(string));
        _dt.Columns.Add("location", typeof(string));
        _dt.Columns.Add("wander_radius", typeof(int));

        using var conn = _db.OpenContent();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, type, location, wander_radius FROM npcs ORDER BY id";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            string id = reader.GetString(0);
            string loc = _db.NpcLocationById(id);
            if (string.IsNullOrWhiteSpace(loc) && !reader.IsDBNull(3)) loc = reader.GetString(3);
            int radius = reader.IsDBNull(4) ? 0 : reader.GetInt32(4);
            _dt.Rows.Add(id, reader.GetString(1), reader.GetString(2), loc, radius);
        }
        Ui.Bind(Grid, _dt);
        Ui.ShowOnly(Grid, "id", "name", "type", "location");
        Ui.MakeComboColumn(Grid, "type", Ui.NpcTypes);
        _win.Status($"NPC: {_dt.Rows.Count}");
    }

    private void SaveWorld()
    {
        try
        {
            Ui.Commit(Grid);
            Db.EnsureId(_dt, "N");
            var npcs = new List<NpcRecord>();
            foreach (DataRow row in _dt.Rows)
            {
                if (row.RowState == DataRowState.Deleted) continue;
                string name = row["name"]?.ToString() ?? "";
                string type = row["type"]?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(type)) continue;
                npcs.Add(new NpcRecord { Id = row["id"]?.ToString() ?? "", Name = name, Type = type, Location = row["location"]?.ToString() ?? "", WanderRadius = ToInt(row["wander_radius"]) });
            }
            SaveNpcsLocal(npcs);
            using var conn = _db.OpenContent();
            foreach (var (key, value) in new[] { ("width", WorldWidth.Text.Trim()), ("height", WorldHeight.Text.Trim()) })
            {
                if (!int.TryParse(value, out int v) || v <= 0) continue;
                ContentStore.UpdateWorldConfig(conn, null, key, v.ToString());
            }
            _db.LoadNpcRefs();
            _db.BuildNpcZoneMapFromTiled();
            LoadWorld();
            _win.Status($"NPC и мир сохранены: {npcs.Count}");
        }
        catch (Exception ex) { _win.Status("Ошибка (мир): " + ex.Message); }
    }

    private void SaveNpcsLocal(List<NpcRecord> npcs)
    {
        var dataMap = new Dictionary<string, string>();
        var posMap = new Dictionary<string, (int X, int Y)>();
        using (var readConn = _db.OpenContent())
        {
            using var cmd = readConn.CreateCommand();
            cmd.CommandText = "SELECT id, data, x, y FROM npcs";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                string id = reader.GetString(0);
                if (!reader.IsDBNull(1)) dataMap[id] = reader.GetString(1);
                int x = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
                int y = reader.IsDBNull(3) ? 0 : reader.GetInt32(3);
                posMap[id] = (x, y);
            }
        }

        using var conn = _db.OpenContent();
        using var transaction = conn.BeginTransaction();
        if (npcs.Count == 0)
            ContentStore.DeleteMissingRows(conn, transaction, "npcs", "id", new List<string>());
        else
            ContentStore.DeleteMissingRows(conn, transaction, "npcs", "id", npcs.Select(n => n.Id));
            foreach (var n in npcs)
            {
                var (exX, exY) = posMap.TryGetValue(n.Id, out var p) ? p : (0, 0);
                string? data = dataMap.TryGetValue(n.Id, out var d) ? d : null;
                ContentStore.UpsertNpc(conn, transaction, n.Id, n.Name, n.Type, exX, exY, n.Location ?? "", data, n.WanderRadius);
            }
        transaction.Commit();
    }

    private void EditSelected()
    {
        if (Ui.SelectedRow(Grid) is not DataRow row)
        {
            _win.Status("Выберите NPC в таблице, затем дважды кликните по нему");
            return;
        }
        Ui.Commit(Grid);
        var dlg = new NpcEditorWindow(_db, row, s => _win.Status(s), OpenDialogueEditor, PlaceNpcOnMap, DuplicateAndPlaceNpc);
        dlg.Owner = Window.GetWindow(this);
        dlg.ShowDialog();
    }

    private void OpenDialogueEditor(DataRow row)
    {
        string id = row["id"]?.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(id))
        {
            _win.Status("Сначала сохраните NPC, чтобы получить ID");
            return;
        }
        var dlg = new DialogueEditorWindow(_db, id);
        dlg.Owner = Window.GetWindow(this);
        dlg.ShowDialog();
        _win.Status("Диалоги закрыты");
    }

    private void PlaceNpcOnMap(DataRow row)
    {
        Ui.Commit(Grid);
        string name = row["name"]?.ToString() ?? "";
        string type = row["type"]?.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(type))
        {
            _win.Status("Заполните имя и тип NPC перед размещением");
            return;
        }
        string id = row["id"]?.ToString() ?? "";
        bool hadId = !string.IsNullOrWhiteSpace(id);

        // Сначала сохраняем строку в БД, иначе после размещения новый NPC
        // «исчезнет»: UpdateNpcLocation не создаёт запись, а LoadWorld прочитает
        // только то, что уже лежит в npcs.
        SaveWorld();

        if (!hadId)
        {
            id = "";
            foreach (DataRow r in _dt.Rows)
            {
                if (r.RowState == DataRowState.Deleted) continue;
                if ((r["name"]?.ToString() ?? "") == name) { id = r["id"]?.ToString() ?? ""; break; }
            }
        }
        if (string.IsNullOrWhiteSpace(id))
        {
            _win.Status("Не удалось определить id NPC");
            return;
        }

        string contentDir = _db.ClientSrcContent();
        if (!Directory.Exists(contentDir))
        {
            _win.Status("Не найдена папка контента клиента: " + contentDir);
            return;
        }

        var dlg = new MapPickerWindow(id, name, type, contentDir);
        dlg.Owner = Window.GetWindow(this);
        if (dlg.ShowDialog() != true) return;

        if (dlg.Cleared)
        {
            using var c = _db.OpenContent();
            ContentStore.UpdateNpcLocation(c, null, id, "");
            _db.LoadNpcRefs();
            _db.BuildNpcZoneMapFromTiled();
            LoadWorld();
            _win.Status($"NPC {id} удалён со всех карт (размещение очищено)");
            return;
        }

        using var conn = _db.OpenContent();
        ContentStore.UpdateNpcLocation(conn, null, id, dlg.PlacedZoneId);

        _db.LoadNpcRefs();
        _db.BuildNpcZoneMapFromTiled();
        LoadWorld();
        _win.Status($"NPC {id} размещён в зоне {dlg.PlacedZoneId} на клетке {dlg.PlacedTileX},{dlg.PlacedTileY}");
    }

    /// <summary>Создаёт независимую копию выбранного NPC (новый id, копия диалога/типа/радиуса)
    /// и сразу открывает окно размещения на карте. Удобно для расстановки нескольких
    /// однотипных бродяг (например, заключённых в камерах) без ручного дублирования строк.</summary>
    private void DuplicateAndPlaceNpc(DataRow sourceRow)
    {
        Ui.Commit(Grid);
        string srcId = sourceRow["id"]?.ToString() ?? "";
        string name = sourceRow["name"]?.ToString() ?? "";
        string type = sourceRow["type"]?.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(type))
        {
            _win.Status("Заполните имя и тип исходного NPC перед дублированием");
            return;
        }
        int radius = ToInt(sourceRow["wander_radius"]);

        // Копируем диалог (data) исходника, чтобы у копии было своё содержимое.
        string? data = null;
        using (var conn = _db.OpenContent())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT data FROM npcs WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", srcId);
            var v = cmd.ExecuteScalar();
            if (v != null && v != System.DBNull.Value) data = v.ToString();
        }

        string newId = Db.NextId(_dt, "N");

        using (var conn = _db.OpenContent())
        using (var tx = conn.BeginTransaction())
        {
            ContentStore.UpsertNpc(conn, tx, newId, name, type, 0, 0, "", data, radius);
            tx.Commit();
        }
        _db.LoadNpcRefs();
        _db.BuildNpcZoneMapFromTiled();
        LoadWorld();

        DataRow? newRow = null;
        foreach (DataRow r in _dt.Rows)
        {
            if (r.RowState == DataRowState.Deleted) continue;
            if ((r["id"]?.ToString() ?? "") == newId) { newRow = r; break; }
        }
        if (newRow == null)
        {
            _win.Status("Не удалось создать копию NPC");
            return;
        }
        PlaceNpcOnMap(newRow);
    }

    private int GetWorldConfigInt(string key, int defaultValue)
    {
        try
        {
            using var conn = _db.OpenContent();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT value FROM world_config WHERE key = $k";
            cmd.Parameters.AddWithValue("$k", key);
            var v = cmd.ExecuteScalar();
            return v == null ? defaultValue : Convert.ToInt32(v);
        }
        catch { return defaultValue; }
    }

    private sealed class NpcRecord
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public string Type { get; init; } = "";
        public string? Location { get; init; }
        public int WanderRadius { get; init; }
    }

    private static int ToInt(object? v) => int.TryParse(v?.ToString(), out int r) ? r : 0;
}
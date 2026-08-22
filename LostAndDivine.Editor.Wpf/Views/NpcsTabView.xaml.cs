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
        DialogueBtn.Click += (s, e) => OpenDialogueEditor();
        PlaceBtn.Click += (s, e) => PlaceSelectedNpcOnMap();
        SaveBtn.Click += (s, e) => SaveWorld();
        Grid.MouseDoubleClick += (s, e) =>
        {
            if (e.OriginalSource is TextBlock or Border)
            {
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) PlaceSelectedNpcOnMap();
                else OpenDialogueEditor();
            }
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

        using var conn = _db.OpenContent();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, type, location FROM npcs ORDER BY id";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            string id = reader.GetString(0);
            string loc = _db.NpcLocationById(id);
            if (string.IsNullOrWhiteSpace(loc) && !reader.IsDBNull(3)) loc = reader.GetString(3);
            _dt.Rows.Add(id, reader.GetString(1), reader.GetString(2), loc);
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
                npcs.Add(new NpcRecord { Id = row["id"]?.ToString() ?? "", Name = name, Type = type, Location = row["location"]?.ToString() ?? "" });
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
            ContentStore.UpsertNpc(conn, transaction, n.Id, n.Name, n.Type, exX, exY, n.Location ?? "", data);
        }
        transaction.Commit();
    }

    private void OpenDialogueEditor()
    {
        if (Ui.SelectedRow(Grid) is not DataRow row)
        {
            _win.Status("Выберите NPC в таблице, затем нажмите «Редактор диалогов...»");
            return;
        }
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

    private void PlaceSelectedNpcOnMap()
    {
        if (Ui.SelectedRow(Grid) is not DataRow row)
        {
            _win.Status("Выберите NPC в таблице, затем нажмите «Разместить на карте...»");
            return;
        }
        Ui.Commit(Grid);
        string name = row["name"]?.ToString() ?? "";
        string type = row["type"]?.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(type))
        {
            _win.Status("Заполните имя и тип NPC перед размещением");
            return;
        }
        string id = row["id"]?.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(id))
        {
            SaveWorld();
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

        using var conn = _db.OpenContent();
        ContentStore.UpdateNpcLocation(conn, null, id, dlg.PlacedZoneId);

        _db.LoadNpcRefs();
        _db.BuildNpcZoneMapFromTiled();
        LoadWorld();
        _win.Status($"NPC {id} размещён в зоне {dlg.PlacedZoneId} на клетке {dlg.PlacedTileX},{dlg.PlacedTileY}");
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
    }
}
using System.Data;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LostAndDivine.Shared.Data;
using Microsoft.Data.Sqlite;

namespace LostAndDivine.Editor.Views;

public partial class MonstersTabView : UserControl
{
    private MainWindow _win = null!;
    private Db _db = null!;
    private DataTable _dt = new();

    public MonstersTabView()
    {
        InitializeComponent();
        Search.TextChanged += (s, e) => Ui.ApplyFilter(Grid, Search.Text);
        AddBtn.Click += (s, e) => Ui.AddRowWithId(Grid, _dt, "M");
        DeleteBtn.Click += (s, e) => Ui.DeleteSelectedRow(Grid);
        EditBtn.Click += (s, e) => EditSelected();
        SaveBtn.Click += (s, e) => Save();
        Grid.MouseDoubleClick += (s, e) => { if (e.OriginalSource is TextBlock or Border) EditSelected(); };
    }

    public void Init(MainWindow win)
    {
        _win = win;
        _db = win.Db;
        LoadMonsters();
    }

    private void LoadMonsters()
    {
        var drops = LoadMonsterDrops(_db.ContentDbFile);
        _dt = _db.LoadTable(@"SELECT id, name, tier, health, phys_attack, phys_defense, xp_reward, gold_reward, gold_max, symbol,
            strength, endurance, agility, cunning, intellect, wisdom, crit_chance, crit_damage, evade_chance, block_chance, parry_chance, shield_defense
            FROM monsters ORDER BY id");
        _dt.Columns.Add("__drops", typeof(string));
        foreach (DataRow r in _dt.Rows)
        {
            string monsterId = r["id"]?.ToString() ?? "";
            r["__drops"] = drops.TryGetValue(monsterId, out var list)
                ? JsonSerializer.Serialize(list.Select(d => new { d.ItemId, d.Chance }))
                : "[]";
        }
        Ui.Bind(Grid, _dt);
        Ui.ShowOnly(Grid, "id", "name", "__drops");
        Ui.ApplyFilter(Grid, Search.Text);
        _win.Status($"Монстры: {_dt.Rows.Count}");
    }

    private static Dictionary<string, List<(string ItemId, int Chance)>> LoadMonsterDrops(string contentDbFile)
    {
        var map = new Dictionary<string, List<(string, int)>>();
        using var conn = new SqliteConnection($"Data Source={contentDbFile}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT monster_id, item_id, drop_chance FROM monster_drops ORDER BY monster_id, item_id";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            string mid = reader.GetString(0);
            if (!map.TryGetValue(mid, out var list))
                map[mid] = list = new List<(string, int)>();
            list.Add((reader.GetString(1), reader.GetInt32(2)));
        }
        return map;
    }

    private void EditSelected()
    {
        if (Ui.SelectedRow(Grid) is DataRow row)
        {
            Ui.Commit(Grid);
            var dlg = new MonsterEditorWindow(_db, row);
            dlg.Owner = Window.GetWindow(this);
            dlg.ShowDialog();
            _win.Status("Монстр изменён");
        }
    }

    private void Save()
    {
        try
        {
            Ui.Commit(Grid);
            Db.EnsureId(_dt, "M");
            using var conn = _db.OpenContent();
            using var transaction = conn.BeginTransaction();
            var monsterIds = new List<string>();
            foreach (DataRow row in _dt.Rows)
            {
                if (row.RowState == DataRowState.Deleted) continue;
                var id = row["id"]?.ToString();
                if (!string.IsNullOrWhiteSpace(id)) monsterIds.Add(id!);
            }
            ContentStore.DeleteMissingRows(conn, transaction, "monsters", "id", monsterIds);
            ContentStore.DeleteAllMonsterDrops(conn, transaction);
            foreach (DataRow row in _dt.Rows)
            {
                if (row.RowState == DataRowState.Deleted) continue;
                if (string.IsNullOrWhiteSpace(row["id"]?.ToString())) continue;
                string monsterId = row["id"].ToString()!;
                ContentStore.UpsertMonster(conn, transaction, row);

                foreach (var (itemId, chance) in ParseDrops(row["__drops"]?.ToString()))
                {
                    if (string.IsNullOrWhiteSpace(itemId)) continue;
                    ContentStore.InsertMonsterDrop(conn, transaction, monsterId, itemId, chance);
                }
            }
            transaction.Commit();
            _db.LoadMonsterRefs();
            _win.Status($"Монстры сохранены: {_dt.Rows.Count}");
        }
        catch (Exception ex) { _win.Status("Ошибка (монстры): " + ex.Message); }
    }

    private static List<(string ItemId, int Chance)> ParseDrops(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]") return new List<(string, int)>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var list = new List<(string, int)>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                string itemId = el.TryGetProperty("ItemId", out var idProp) ? idProp.GetString() ?? "" : "";
                int chance = el.TryGetProperty("Chance", out var cProp) ? cProp.GetInt32() : 0;
                list.Add((itemId, chance));
            }
            return list;
        }
        catch { return new List<(string, int)>(); }
    }
}
using System.Data;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
            Db.DeleteMissingRows(conn, transaction, "monsters", "id", monsterIds);
            using (var delDrops = conn.CreateCommand()) { delDrops.CommandText = "DELETE FROM monster_drops"; delDrops.ExecuteNonQuery(); }
            foreach (DataRow row in _dt.Rows)
            {
                if (row.RowState == DataRowState.Deleted) continue;
                if (string.IsNullOrWhiteSpace(row["id"]?.ToString())) continue;
                string monsterId = row["id"].ToString()!;
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"INSERT OR REPLACE INTO monsters (id, name, tier, health, phys_attack, phys_defense, xp_reward, gold_reward, gold_max, symbol,
                        strength, endurance, agility, cunning, intellect, wisdom, crit_chance, crit_damage, evade_chance,
                        block_chance, parry_chance, shield_defense)
                    VALUES ($id,$n,$t,$hp,$a,$d,$xp,$g,$gm,$s,$str,$sta,$agi,$cun,$wis,$wil,$cc,$cd,$ec,$bc,$pc,$sd)";
                cmd.Parameters.AddWithValue("$id", monsterId);
                cmd.Parameters.AddWithValue("$n", row["name"] ?? "");
                cmd.Parameters.AddWithValue("$t", Db.ToInt(row["tier"]));
                cmd.Parameters.AddWithValue("$hp", Db.ToInt(row["health"]));
                cmd.Parameters.AddWithValue("$a", Db.ToInt(row["phys_attack"]));
                cmd.Parameters.AddWithValue("$d", Db.ToInt(row["phys_defense"]));
                cmd.Parameters.AddWithValue("$xp", Db.ToInt(row["xp_reward"]));
                cmd.Parameters.AddWithValue("$g", Db.ToInt(row["gold_reward"]));
                cmd.Parameters.AddWithValue("$gm", Db.ToInt(row["gold_max"]));
                cmd.Parameters.AddWithValue("$s", (row["symbol"]?.ToString() ?? "M").Length > 0 ? row["symbol"].ToString()![0].ToString() : "M");
                cmd.Parameters.AddWithValue("$str", Db.ToInt(row["strength"]));
                cmd.Parameters.AddWithValue("$sta", Db.ToInt(row["endurance"]));
                cmd.Parameters.AddWithValue("$agi", Db.ToInt(row["agility"]));
                cmd.Parameters.AddWithValue("$cun", Db.ToInt(row["cunning"]));
                cmd.Parameters.AddWithValue("$wis", Db.ToInt(row["intellect"]));
                cmd.Parameters.AddWithValue("$wil", Db.ToInt(row["wisdom"]));
                cmd.Parameters.AddWithValue("$cc", Db.ToDouble(row["crit_chance"]));
                cmd.Parameters.AddWithValue("$cd", Db.ToDouble(row["crit_damage"]));
                cmd.Parameters.AddWithValue("$ec", Db.ToDouble(row["evade_chance"]));
                cmd.Parameters.AddWithValue("$bc", Db.ToDouble(row["block_chance"]));
                cmd.Parameters.AddWithValue("$pc", Db.ToDouble(row["parry_chance"]));
                cmd.Parameters.AddWithValue("$sd", Db.ToInt(row["shield_defense"]));
                cmd.ExecuteNonQuery();

                foreach (var (itemId, chance) in ParseDrops(row["__drops"]?.ToString()))
                {
                    if (string.IsNullOrWhiteSpace(itemId)) continue;
                    using var dropCmd = conn.CreateCommand();
                    dropCmd.CommandText = "INSERT INTO monster_drops (monster_id, item_id, drop_chance) VALUES ($mid, $iid, $dc)";
                    dropCmd.Parameters.AddWithValue("$mid", monsterId);
                    dropCmd.Parameters.AddWithValue("$iid", itemId);
                    dropCmd.Parameters.AddWithValue("$dc", Math.Clamp(chance, 0, 100));
                    dropCmd.ExecuteNonQuery();
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
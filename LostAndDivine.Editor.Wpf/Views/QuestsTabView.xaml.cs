using System.Data;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LostAndDivine.Shared.Data;
using Microsoft.Data.Sqlite;

namespace LostAndDivine.Editor.Views;

public partial class QuestsTabView : UserControl
{
    private MainWindow _win = null!;
    private Db _db = null!;
    private DataTable _dt = new();

    public QuestsTabView()
    {
        InitializeComponent();
        Search.TextChanged += (s, e) => Ui.ApplyFilter(Grid, Search.Text);
        AddBtn.Click += (s, e) => AddQuest();
        DeleteBtn.Click += (s, e) => Ui.DeleteSelectedRow(Grid);
        EditBtn.Click += (s, e) => EditSelected();
        SaveBtn.Click += (s, e) => Save();
        Grid.MouseDoubleClick += (s, e) => { if (e.OriginalSource is TextBlock or Border) EditSelected(); };
    }

    public void Init(MainWindow win)
    {
        _win = win;
        _db = win.Db;
        LoadQuests();
    }

    private void LoadQuests()
    {
        _dt = new DataTable();
        _dt.Columns.Add("id", typeof(string));
        _dt.Columns.Add("title", typeof(string));
        _dt.Columns.Add("description", typeof(string));
        _dt.Columns.Add("type", typeof(string));
        _dt.Columns.Add("monster", typeof(string));
        _dt.Columns.Add("item", typeof(string));
        _dt.Columns.Add("use_item", typeof(string));
        _dt.Columns.Add("npc", typeof(string));
        _dt.Columns.Add("target_zone", typeof(string));
        _dt.Columns.Add("target_x", typeof(string));
        _dt.Columns.Add("target_y", typeof(string));
        _dt.Columns.Add("target", typeof(string));
        _dt.Columns.Add("xp_reward", typeof(string));
        _dt.Columns.Add("gold_reward", typeof(string));
        _dt.Columns.Add("chain_id", typeof(string));
        _dt.Columns.Add("step", typeof(string));
        _dt.Columns.Add("prereq", typeof(string));
        _dt.Columns.Add("min_level", typeof(string));
        _dt.Columns.Add("item_reward", typeof(string));
        _dt.Columns.Add("item_reward_count", typeof(string));
        _dt.Columns.Add("auto_grant", typeof(bool));
        _dt.Columns.Add("giver_npc", typeof(string));
        _dt.Columns.Add("is_story", typeof(bool));
        _dt.Columns.Add("repeatable", typeof(bool));
        _dt.Columns.Add("location", typeof(string));
        _dt.Columns.Add("objectives", typeof(string));

        using var conn = _db.OpenContent();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT id, title, description, type, target_monster_id, target_item_id, target_npc_id, target,
                xp_reward, gold_reward, chain_id, step, prerequisite_quest_id, min_level, item_reward_id, item_reward_count,
                target_zone_id, target_x, target_y, auto_grant, giver_npc_id, is_story, location, repeatable, objectives
            FROM quests_def ORDER BY id";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            string mid = reader.IsDBNull(4) ? "" : reader.GetString(4);
            string iid = reader.IsDBNull(5) ? "" : reader.GetString(5);
            string nid = reader.IsDBNull(6) ? "" : reader.GetString(6);
            string ch = reader.IsDBNull(10) ? "" : reader.GetString(10);
            string pr = reader.IsDBNull(12) ? "" : reader.GetString(12);
            string rid = reader.IsDBNull(14) ? "" : reader.GetString(14);
            string zone = reader.IsDBNull(16) ? "" : reader.GetString(16);
            string gid = reader.IsDBNull(20) ? "" : reader.GetString(20);
            string derivedLoc = !string.IsNullOrEmpty(gid) ? _db.NpcLocationById(gid)
                : !string.IsNullOrEmpty(nid) ? _db.NpcLocationById(nid)
                : (reader.IsDBNull(22) ? "" : reader.GetString(22));
            _dt.Rows.Add(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                Db.NameById(_db.MonsterRefs, mid), Db.NameById(_db.CollectibleRefs, iid), Db.NameById(_db.RewardItemRefs, iid),
                Db.NameById(_db.NpcRefs, nid),
                zone, reader.GetInt32(17).ToString(), reader.GetInt32(18).ToString(),
                reader.GetInt32(7).ToString(), reader.GetInt32(8).ToString(), reader.GetInt32(9).ToString(),
                ch, reader.GetInt32(11).ToString(), pr, reader.GetInt32(13).ToString(),
                Db.NameById(_db.RewardItemRefs, rid), reader.GetInt32(15).ToString(),
                !reader.IsDBNull(19) && reader.GetInt32(19) != 0,
                Db.NameById(_db.NpcRefs, gid),
                !reader.IsDBNull(21) && reader.GetInt32(21) != 0,
                !reader.IsDBNull(23) ? reader.GetInt32(23) != 0 : false,
                derivedLoc,
                reader.IsDBNull(24) ? "" : reader.GetString(24));
        }

        Ui.Bind(Grid, _dt);
        Ui.ShowOnly(Grid, "id", "title", "giver_npc", "is_story", "repeatable", "location", "type", "objectives");
        Ui.MakeComboColumn(Grid, "giver_npc", _db.NpcRefs.Select(r => r.Name).ToList());
        Ui.MakeComboColumn(Grid, "type", Ui.QuestTypes);
        Ui.ApplyFilter(Grid, Search.Text);
        _win.Status($"Квесты: {_dt.Rows.Count}");
    }

    private void AddQuest()
    {
        object?[] cells = new object?[_dt.Columns.Count];
        for (int i = 0; i < cells.Length; i++) cells[i] = "";
        cells[0] = Db.NextId(_dt, "Q");
        cells[3] = "kill";
        _dt.Rows.Add(cells);
        Ui.SelectLastRow(Grid, _dt);
    }

    private void EditSelected()
    {
        if (Ui.SelectedRow(Grid) is DataRow row)
        {
            Ui.Commit(Grid);
            var dlg = new QuestEditorWindow(_db, row);
            dlg.Owner = Window.GetWindow(this);
            dlg.ShowDialog();
            _win.Status("Квест изменён");
        }
    }

    private void Save()
    {
        try
        {
            Ui.Commit(Grid);
            Db.EnsureId(_dt, "Q");
            using var conn = _db.OpenContent();
            using var transaction = conn.BeginTransaction();
            var questIds = new List<string>();
            foreach (DataRow row in _dt.Rows)
            {
                if (row.RowState == DataRowState.Deleted) continue;
                var id = row["id"]?.ToString();
                if (!string.IsNullOrWhiteSpace(id)) questIds.Add(id!);
            }
            ContentStore.DeleteMissingRows(conn, transaction, "quests_def", "id", questIds);
            foreach (DataRow row in _dt.Rows)
            {
                if (row.RowState == DataRowState.Deleted) continue;
                if (string.IsNullOrWhiteSpace(row["id"]?.ToString())) continue;
                string type = row["type"]?.ToString() ?? "kill";
                string monsterId = type == "kill" ? Db.IdByName(_db.MonsterRefs, row["monster"]?.ToString() ?? "") : "";
                string itemId = type == "collect" ? Db.IdByName(_db.CollectibleRefs, row["item"]?.ToString() ?? "")
                    : type == "use" ? Db.IdByName(_db.RewardItemRefs, row["use_item"]?.ToString() ?? "") : "";
                string npcId = Db.IdByName(_db.NpcRefs, row["npc"]?.ToString() ?? "");
                string giverId = Db.IdByName(_db.NpcRefs, row["giver_npc"]?.ToString() ?? "");
                string objectives = row["objectives"]?.ToString()?.Trim() ?? "";
                if (string.IsNullOrEmpty(objectives) || objectives == "[]")
                {
                    string objTarget = type switch
                    {
                        "kill" => monsterId,
                        "collect" or "use" => itemId,
                        "talk" or "travel" => npcId,
                        "explore" => row["target_zone"]?.ToString() ?? "",
                        _ => ""
                    };
                    var obj = new Dictionary<string, object>
                    {
                        ["type"] = type,
                        ["target"] = objTarget,
                        ["count"] = Math.Max(1, Db.ToInt(row["target"]))
                    };
                    if (type == "travel" && string.IsNullOrEmpty(npcId))
                    {
                        obj["targetX"] = Db.ToInt(row["target_x"]);
                        obj["targetY"] = Db.ToInt(row["target_y"]);
                    }
                    objectives = JsonSerializer.Serialize(new[] { obj }, Db.QuestJsonOpts);
                }
                ContentStore.UpsertQuest(conn, transaction,
                    row["id"]?.ToString() ?? "",
                    row["title"]?.ToString() ?? "",
                    row["description"]?.ToString() ?? "",
                    type,
                    monsterId, itemId, npcId,
                    Db.ToInt(row["target"]),
                    Db.ToInt(row["xp_reward"]),
                    Db.ToInt(row["gold_reward"]),
                    row["chain_id"]?.ToString() ?? "",
                    Db.ToInt(row["step"]),
                    row["prereq"]?.ToString() ?? "",
                    Db.ToInt(row["min_level"]),
                    Db.IdByName(_db.RewardItemRefs, row["item_reward"]?.ToString() ?? ""),
                    Db.ToInt(row["item_reward_count"]),
                    row["target_zone"]?.ToString() ?? "",
                    Db.ToInt(row["target_x"]),
                    Db.ToInt(row["target_y"]),
                    row["auto_grant"] is bool ag && ag ? 1 : 0,
                    giverId,
                    row["is_story"] is bool ist && ist ? 1 : 0,
                    row["location"]?.ToString() ?? "",
                    (row["is_story"] is bool iss && iss) ? 0
                        : (row["repeatable"] is bool repb && repb ? 1 : 0),
                    objectives);
            }
            transaction.Commit();
            _db.LoadQuestRefs();
            _win.Status($"Квесты сохранены: {_dt.Rows.Count}");
        }
        catch (Exception ex) { _win.Status("Ошибка (квесты): " + ex.Message); }
    }
}
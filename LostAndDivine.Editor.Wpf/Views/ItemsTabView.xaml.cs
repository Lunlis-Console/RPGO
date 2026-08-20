using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Data.Sqlite;

namespace LostAndDivine.Editor.Views;

public partial class ItemsTabView : UserControl
{
    private MainWindow _win = null!;
    private Db _db = null!;
    private DataTable _dt = new();

    public ItemsTabView()
    {
        InitializeComponent();
        TypeFilter.ItemsSource = new[] { "все" }.Concat(Ui.ItemTypesLocalized.Select(p => p.Value)).ToList();
        TypeFilter.SelectedIndex = 0;
        Search.TextChanged += (s, e) => ApplyFilter();
        TypeFilter.SelectionChanged += (s, e) => ApplyFilter();
        AddBtn.Click += (s, e) => Ui.AddRowWithId(Grid, _dt, "I");
        DeleteBtn.Click += (s, e) => Ui.DeleteSelectedRow(Grid);
        EditBtn.Click += (s, e) => EditSelected();
        SaveBtn.Click += (s, e) => Save();
        Grid.MouseDoubleClick += (s, e) => { if (e.OriginalSource is TextBlock or Border) EditSelected(); };
    }

    public void Init(MainWindow win)
    {
        _win = win;
        _db = win.Db;
        LoadItems();
    }

    private void LoadItems()
    {
        _dt = _db.LoadTable(@"SELECT id, name, type, value, damage_min, damage_max, defense, max_health_bonus, max_mana_bonus, heal_amount, restore_mana, stock, description,
            bonus_strength, bonus_endurance, bonus_agility, bonus_cunning, bonus_intellect, bonus_wisdom,
            bonus_phys_attack, bonus_mag_attack, bonus_defense, bonus_resistance,
            bonus_attack_speed, bonus_crit_chance, bonus_crit_damage, bonus_evade_chance,
            bonus_block_chance, bonus_parry_chance, bonus_accuracy, bonus_tenacity,
            bonus_armor_penetration, bonus_cooldown_reduction, bonus_hp_regen, bonus_mp_regen,
            two_handed, damage_type, attack_speed_modifier, weapon_subtype, attack_range, required_level,
            quest_item, icon, magic_defense, quality, roll_config
            FROM items ORDER BY id");

        // quest_item как чекбокс
        if (_dt.Columns["quest_item"] is DataColumn qcol)
        {
            var boolCol = _dt.Columns.Add("__qi", typeof(bool));
            foreach (DataRow r in _dt.Rows)
                r["__qi"] = !r.IsNull(qcol) && Convert.ToString(r[qcol]) == "1";
            _dt.Columns.Remove(qcol);
            boolCol.ColumnName = "quest_item";
        }

        // Обзорная таблица: только основные поля; остальное — в окне редактирования предмета.
        Grid.AutoGenerateColumns = false;
        Grid.Columns.Clear();
        Grid.Columns.Add(new DataGridTextColumn { Header = "id", Binding = new Binding("id") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = new DataGridLength(90) });
        Grid.Columns.Add(new DataGridTextColumn { Header = "name", Binding = new Binding("name") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
        Grid.Columns.Add(new DataGridComboBoxColumn { Header = "type", ItemsSource = Ui.ItemTypesLocalized, DisplayMemberPath = "Value", SelectedValuePath = "Key", SelectedValueBinding = new Binding("type") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged } });
        Grid.Columns.Add(new DataGridTextColumn { Header = "треб. ур.", Binding = new Binding("required_level") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = new DataGridLength(70) });
        Grid.Columns.Add(new DataGridCheckBoxColumn { Header = "квест", Binding = new Binding("quest_item") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = new DataGridLength(60) });
        Grid.ItemsSource = _dt.DefaultView;
        ApplyFilter();
        _win.Status($"Предметы: {_dt.Rows.Count}");
    }

    private void ApplyFilter()
    {
        if (Ui.View(Grid) is not DataView dv) return;
        string search = Search.Text.Trim();
        string type = TypeFilter.SelectedItem?.ToString() ?? "все";
        string filter = "";
        if (type != "все")
        {
            string typeVal = Ui.ItemTypeValue(type);
            filter = $"type = '{typeVal.Replace("'", "''")}'";
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            string escaped = search.Replace("'", "''");
            string sFilter = $"(name LIKE '%{escaped}%' OR id LIKE '%{escaped}%')";
            filter = string.IsNullOrEmpty(filter) ? sFilter : $"({filter}) AND {sFilter}";
        }
        dv.RowFilter = filter;
    }

    private void EditSelected()
    {
        if (Ui.SelectedRow(Grid) is DataRow row)
        {
            Ui.Commit(Grid);
            var dlg = new ItemEditorWindow(_db, row, _dt);
            dlg.Owner = Window.GetWindow(this);
            dlg.ShowDialog();
            _win.Status("Предмет изменён");
        }
    }

    private void Save()
    {
        try
        {
            Ui.Commit(Grid);
            Db.EnsureId(_dt, "I");
            using var conn = _db.OpenContent();
            using var transaction = conn.BeginTransaction();
            using (var del = conn.CreateCommand()) { del.CommandText = "DELETE FROM items"; del.ExecuteNonQuery(); }
            foreach (DataRow row in _dt.Rows)
            {
                if (row.RowState == DataRowState.Deleted) continue;
                if (string.IsNullOrWhiteSpace(row["id"]?.ToString())) continue;
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"INSERT INTO items (id, name, type, value, damage_min, damage_max, defense, max_health_bonus, max_mana_bonus, heal_amount, restore_mana, stock, description,
                        bonus_strength, bonus_endurance, bonus_agility, bonus_cunning, bonus_intellect, bonus_wisdom,
                        bonus_phys_attack, bonus_mag_attack, bonus_defense, bonus_resistance,
                        bonus_attack_speed, bonus_crit_chance, bonus_crit_damage, bonus_evade_chance,
                        bonus_block_chance, bonus_parry_chance, bonus_accuracy, bonus_tenacity,
                        bonus_armor_penetration, bonus_cooldown_reduction, bonus_hp_regen, bonus_mp_regen,
                        two_handed,
                        damage_type, attack_speed_modifier, weapon_subtype, attack_range, required_level, quest_item, icon, magic_defense, quality, roll_config)
                    VALUES ($id,$n,$t,$v,$dmn,$dmx,$d,$m,$mm,$h,$rm,$s,$desc,$str,$sta,$agi,$cun,$wis,$wil,$bpa,$bma,$bdef,$bres,$bas,$cc,$cd,$ec,$blk,$prr,$acc,$ten,$arp,$cdr,$hpr,$mpr,$th,$dt,$asm,$ws,$ar,$rl,$qi,$ic,$md,$q,$rc)";
                cmd.Parameters.AddWithValue("$id", row["id"]);
                cmd.Parameters.AddWithValue("$n", row["name"] ?? "");
                cmd.Parameters.AddWithValue("$t", row["type"] ?? "");
                cmd.Parameters.AddWithValue("$v", Db.ToInt(row["value"]));
                cmd.Parameters.AddWithValue("$dmn", Db.ToInt(row["damage_min"]));
                cmd.Parameters.AddWithValue("$dmx", Db.ToInt(row["damage_max"]));
                cmd.Parameters.AddWithValue("$d", Db.ToInt(row["defense"]));
                cmd.Parameters.AddWithValue("$m", Db.ToInt(row["max_health_bonus"]));
                cmd.Parameters.AddWithValue("$mm", Db.ToInt(row["max_mana_bonus"]));
                cmd.Parameters.AddWithValue("$h", Db.ToInt(row["heal_amount"]));
                cmd.Parameters.AddWithValue("$rm", Db.ToInt(row["restore_mana"]));
                cmd.Parameters.AddWithValue("$s", Db.ToInt(row["stock"]));
                cmd.Parameters.AddWithValue("$desc", row["description"] ?? "");
                cmd.Parameters.AddWithValue("$str", Db.ToInt(row["bonus_strength"]));
                cmd.Parameters.AddWithValue("$sta", Db.ToInt(row["bonus_endurance"]));
                cmd.Parameters.AddWithValue("$agi", Db.ToInt(row["bonus_agility"]));
                cmd.Parameters.AddWithValue("$cun", Db.ToInt(row["bonus_cunning"]));
                cmd.Parameters.AddWithValue("$wis", Db.ToInt(row["bonus_intellect"]));
                cmd.Parameters.AddWithValue("$wil", Db.ToInt(row["bonus_wisdom"]));
                cmd.Parameters.AddWithValue("$bpa", Db.ToInt(row["bonus_phys_attack"]));
                cmd.Parameters.AddWithValue("$bma", Db.ToInt(row["bonus_mag_attack"]));
                cmd.Parameters.AddWithValue("$bdef", Db.ToInt(row["bonus_defense"]));
                cmd.Parameters.AddWithValue("$bres", Db.ToInt(row["bonus_resistance"]));
                cmd.Parameters.AddWithValue("$bas", Db.ToDouble(row["bonus_attack_speed"]));
                cmd.Parameters.AddWithValue("$cc", Db.ToDouble(row["bonus_crit_chance"]));
                cmd.Parameters.AddWithValue("$cd", Db.ToDouble(row["bonus_crit_damage"]));
                cmd.Parameters.AddWithValue("$ec", Db.ToDouble(row["bonus_evade_chance"]));
                cmd.Parameters.AddWithValue("$blk", Db.ToDouble(row["bonus_block_chance"]));
                cmd.Parameters.AddWithValue("$prr", Db.ToDouble(row["bonus_parry_chance"]));
                cmd.Parameters.AddWithValue("$acc", Db.ToDouble(row["bonus_accuracy"]));
                cmd.Parameters.AddWithValue("$ten", Db.ToDouble(row["bonus_tenacity"]));
                cmd.Parameters.AddWithValue("$arp", Db.ToDouble(row["bonus_armor_penetration"]));
                cmd.Parameters.AddWithValue("$cdr", Db.ToDouble(row["bonus_cooldown_reduction"]));
                cmd.Parameters.AddWithValue("$hpr", Db.ToDouble(row["bonus_hp_regen"]));
                cmd.Parameters.AddWithValue("$mpr", Db.ToDouble(row["bonus_mp_regen"]));
                cmd.Parameters.AddWithValue("$th", Db.ToInt(row["two_handed"]));
                cmd.Parameters.AddWithValue("$dt", row["damage_type"] ?? "");
                cmd.Parameters.AddWithValue("$asm", Db.ToDouble(row["attack_speed_modifier"]));
                cmd.Parameters.AddWithValue("$ws", row["weapon_subtype"] ?? "");
                cmd.Parameters.AddWithValue("$ar", Db.ToInt(row["attack_range"]));
                cmd.Parameters.AddWithValue("$rl", Db.ToInt(row["required_level"]));
                cmd.Parameters.AddWithValue("$qi", Db.QuestFlag(row["quest_item"]));
                cmd.Parameters.AddWithValue("$ic", row["icon"]?.ToString() ?? "");
                cmd.Parameters.AddWithValue("$md", Db.ToInt(row["magic_defense"]));
                cmd.Parameters.AddWithValue("$q", Db.ToInt(row["quality"]));
                cmd.Parameters.AddWithValue("$rc", row["roll_config"]?.ToString() ?? "");
                cmd.ExecuteNonQuery();
            }
            transaction.Commit();
            _db.LoadCollectibleRefs();
            _db.LoadRewardItemRefs();
            _win.Status($"Предметы сохранены: {_dt.Rows.Count}");
        }
        catch (Exception ex) { _win.Status("Ошибка (предметы): " + ex.Message); }
    }
}
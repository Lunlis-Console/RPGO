using System.Data;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using RPGGame.Shared.Migrations;

namespace RPGGame.Editor;

public partial class MainForm : Form
{
    private readonly string _dbFile;
    private TabControl _tabs = null!;
    private DataGridView _itemsGrid = null!;
    private DataGridView _monstersGrid = null!;
    private DataGridView _questsGrid = null!;
    private Label _status = null!;

    private List<(string Id, string Name)> _monsterRefs = new();
    private List<(string Id, string Name)> _collectibleRefs = new();
    private DataGridView _worldGrid = null!;
    private DataGridView _lootGrid = null!;
    private int _worldWidth = 100;
    private int _worldHeight = 100;
    private ComboBox _itemTypeSelector = null!;
    private CheckedListBox _merchantStockList = null!;

    // --- Анимации (спрайт-листы) ---
    private DataGridView _animGrid = null!;
    private PictureBox _animPreview = null!;
    private System.Windows.Forms.Timer _animTimer = null!;
    private Button _animAddBtn = null!;
    private Button _animDelBtn = null!;
    private Button _animSaveBtn = null!;
    // Оригинальный путь к выбранному PNG (чтобы копировать его при сохранении)
    private readonly Dictionary<string, string> _animSrcPaths = new();
    private System.Drawing.Image? _animPreviewImage;
    private readonly Stopwatch _animStopwatch = new();

    public MainForm(string dbFile)
    {
        _dbFile = dbFile;
        Text = "Редактор SimpleRPG — " + Path.GetFileName(dbFile);
        Size = new Size(1000, 620);
        StartPosition = FormStartPosition.CenterScreen;
        InitializeUI();
        LoadAll();
    }

    private void InitializeUI()
    {
        _tabs = new TabControl { Dock = DockStyle.Fill };

        // --- Предметы ---
        var itemsTab = new TabPage("Предметы");
        var itemsPanel = new Panel { Dock = DockStyle.Fill };

        var itemsTop = new Panel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(6, 6, 6, 6) };
        var typeLabel = new Label { Text = "Показывать поля для типа:", Dock = DockStyle.Left, Width = 170, TextAlign = ContentAlignment.MiddleLeft };
        _itemTypeSelector = new ComboBox
        {
            Dock = DockStyle.Left,
            Width = 150,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Items = { "все", "weapon", "twohand", "shield", "helmet", "cloak", "chest", "legs", "boots", "glove_r", "glove_l", "necklace", "ring", "accessory", "consumable", "collectible", "trophy" },
        };
        _itemTypeSelector.SelectedIndex = 0;
        _itemTypeSelector.SelectedIndexChanged += (s, e) => ApplyItemTypeView();
        itemsTop.Controls.Add(_itemTypeSelector);
        itemsTop.Controls.Add(typeLabel);

        _itemsGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = true,
            AllowUserToDeleteRows = true,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        };
        _itemsGrid.RowsAdded += (s, e) =>
        {
            // Если выбран конкретный тип, новым строкам сразу ставим его
            string sel = _itemTypeSelector?.SelectedItem?.ToString() ?? "все";
            if (sel == "все" || _itemsGrid.Columns["type"] == null) return;
            for (int i = e.RowIndex; i < e.RowIndex + e.RowCount; i++)
            {
                if (i >= 0 && i < _itemsGrid.Rows.Count)
                {
                    var row = _itemsGrid.Rows[i];
                    if (row.Cells["type"].Value == null || string.IsNullOrWhiteSpace(row.Cells["type"].Value?.ToString()))
                        row.Cells["type"].Value = sel;
                }
            }
        };
        _itemsGrid.CellValueChanged += (s, e) =>
        {
            if (e.ColumnIndex < 0 || e.RowIndex < 0) return;
            var col = _itemsGrid.Columns[e.ColumnIndex];
            if (col?.Name != "type") return;
            var row = _itemsGrid.Rows[e.RowIndex];
            string t = row.Cells["type"].Value?.ToString() ?? "";
            // Список колонок, которые нужно обнулить для данного типа
            var toClear = t switch
            {
                "collectible" => new[] { "attack", "defense", "max_health_bonus", "heal_amount",
                    "bonus_strength", "bonus_stamina", "bonus_agility", "bonus_cunning", "bonus_wisdom", "bonus_will", "bonus_crit_chance", "bonus_crit_damage", "bonus_evade_chance" },
                "consumable" => new[] { "attack", "defense",
                    "bonus_strength", "bonus_stamina", "bonus_agility", "bonus_cunning", "bonus_wisdom", "bonus_will", "bonus_crit_chance", "bonus_crit_damage", "bonus_evade_chance" },
                "weapon" => new[] { "heal_amount", "bonus_cunning", "bonus_wisdom" },
                "armor" => new[] { "heal_amount", "bonus_cunning", "bonus_wisdom" },
                _ => Array.Empty<string>()
            };
            foreach (var c in toClear)
            {
                if (_itemsGrid.Columns.Contains(c) && row.Cells[c] is DataGridViewCell cell)
                    cell.Value = 0;
            }
        };
        itemsPanel.Controls.Add(_itemsGrid);
        itemsPanel.Controls.Add(itemsTop);
        var itemsBtn = new Button { Text = "Сохранить предметы", Dock = DockStyle.Bottom, Height = 32 };
        itemsBtn.Click += (s, e) => SaveItems();
        itemsPanel.Controls.Add(itemsBtn);
        itemsTab.Controls.Add(itemsPanel);

        // --- Монстры ---
        var monstersTab = new TabPage("Монстры");
        _monstersGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = true,
            AllowUserToDeleteRows = true,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        };
        var monstersPanel = new Panel { Dock = DockStyle.Fill };
        monstersPanel.Controls.Add(_monstersGrid);
        var monstersBtn = new Button { Text = "Сохранить монстров", Dock = DockStyle.Bottom, Height = 32 };
        monstersBtn.Click += (s, e) => SaveMonsters();
        monstersPanel.Controls.Add(monstersBtn);
        monstersTab.Controls.Add(monstersPanel);

        // --- Лут ---
        var lootTab = new TabPage("Лут");
        _lootGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = true,
            AllowUserToDeleteRows = true,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        };
        var lootPanel = new Panel { Dock = DockStyle.Fill };
        lootPanel.Controls.Add(_lootGrid);
        var lootBtn = new Button { Text = "Сохранить лут", Dock = DockStyle.Bottom, Height = 32 };
        lootBtn.Click += (s, e) => SaveLoot();
        lootPanel.Controls.Add(lootBtn);
        lootTab.Controls.Add(lootPanel);

        // --- Квесты ---
        var questsTab = new TabPage("Квесты");
        _questsGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = true,
            AllowUserToDeleteRows = true,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        };
        var questsPanel = new Panel { Dock = DockStyle.Fill };
        questsPanel.Controls.Add(_questsGrid);
        var questsBtn = new Button { Text = "Сохранить квесты", Dock = DockStyle.Bottom, Height = 32 };
        questsBtn.Click += (s, e) => SaveQuests();
        questsPanel.Controls.Add(questsBtn);
        questsTab.Controls.Add(questsPanel);

        // --- Мир (NPC + размер карты) ---
        var worldTab = new TabPage("Мир");
        var worldPanel = new Panel { Dock = DockStyle.Fill };

        _worldGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = true,
            AllowUserToDeleteRows = true,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        };
        worldPanel.Controls.Add(_worldGrid);

        var worldBtn = new Button { Text = "Сохранить NPC и мир", Dock = DockStyle.Bottom, Height = 32 };
        worldBtn.Click += (s, e) => SaveWorld();
        worldPanel.Controls.Add(worldBtn);
        worldTab.Controls.Add(worldPanel);

        // --- Торговец (ассортимент) ---
        var merchantTab = new TabPage("Торговец");
        var merchantPanel = new Panel { Dock = DockStyle.Fill };

        var merchantTop = new Panel { Dock = DockStyle.Top, Height = 28, Padding = new Padding(6, 2, 6, 2) };
        merchantTop.Controls.Add(new Label
        {
            Text = "Отметьте предметы, которые продаёт торговец:",
            Dock = DockStyle.Left,
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft
        });

        _merchantStockList = new CheckedListBox
        {
            Dock = DockStyle.Fill,
            CheckOnClick = true,
            Font = new Font("Segoe UI", 10)
        };

        merchantPanel.Controls.Add(_merchantStockList);
        merchantPanel.Controls.Add(merchantTop);

        var merchantBtn = new Button { Text = "Сохранить ассортимент", Dock = DockStyle.Bottom, Height = 32 };
        merchantBtn.Click += (s, e) => SaveMerchantStockEditor();
        merchantPanel.Controls.Add(merchantBtn);
        merchantTab.Controls.Add(merchantPanel);

        _tabs.TabPages.Add(itemsTab);
        _tabs.TabPages.Add(monstersTab);
        _tabs.TabPages.Add(lootTab);
        _tabs.TabPages.Add(questsTab);
        _tabs.TabPages.Add(worldTab);
        _tabs.TabPages.Add(merchantTab);
        _tabs.TabPages.Add(BuildAnimationsTab());

        Controls.Add(_tabs);

        _status = new Label { Dock = DockStyle.Bottom, Height = 22, Text = "Готово", BorderStyle = BorderStyle.Fixed3D };
        Controls.Add(_status);
    }

    private void LoadAll()
    {
        var connStr = $"Data Source={_dbFile}";
        DbMigrationRunner.RunMigrations(connStr);

        LoadMonsterRefs();
        LoadCollectibleRefs();
        LoadItems();
        LoadMonsters();
        LoadLoot();
        LoadQuests();
        LoadWorld();
        LoadMerchantStockEditor();
    }

    private void LoadMonsterRefs()
    {
        _monsterRefs = LoadRefs("SELECT id, name FROM monsters ORDER BY id");
    }

    private void LoadCollectibleRefs()
    {
        _collectibleRefs = LoadRefs("SELECT id, name FROM items WHERE type='collectible' ORDER BY id");
    }

    private List<(string Id, string Name)> LoadRefs(string query)
    {
        var list = new List<(string, string)>();
        using var conn = new SqliteConnection($"Data Source={_dbFile}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = query;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add((reader.GetString(0), reader.GetString(1)));
        return list;
    }

    private void LoadQuests()
    {
        BuildQuestsGrid();

        var dt = new DataTable();
        dt.Columns.Add("id", typeof(string));
        dt.Columns.Add("title", typeof(string));
        dt.Columns.Add("description", typeof(string));
        dt.Columns.Add("type", typeof(string));
        dt.Columns.Add("monster", typeof(string));      // отображаем имя
        dt.Columns.Add("item", typeof(string));          // отображаем имя
        dt.Columns.Add("target", typeof(string));
        dt.Columns.Add("xp_reward", typeof(string));
        dt.Columns.Add("gold_reward", typeof(string));

        using var conn = new SqliteConnection($"Data Source={_dbFile}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, title, description, type, target_monster_id, target_item_id, target, xp_reward, gold_reward FROM quests_def ORDER BY id";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            string mid = reader.IsDBNull(4) ? "" : reader.GetString(4);
            string iid = reader.IsDBNull(5) ? "" : reader.GetString(5);
            dt.Rows.Add(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                NameById(_monsterRefs, mid),
                NameById(_collectibleRefs, iid),
                reader.GetInt32(6).ToString(),
                reader.GetInt32(7).ToString(),
                reader.GetInt32(8).ToString());
        }
        _questsGrid.DataSource = dt;
    }

    private void BuildQuestsGrid()
    {
        _questsGrid.Columns.Clear();
        _questsGrid.AutoGenerateColumns = false;

        void AddText(string name, string header)
        {
            var c = new DataGridViewTextBoxColumn { DataPropertyName = name, HeaderText = header, Name = name };
            _questsGrid.Columns.Add(c);
        }

        AddText("id", "ID");
        AddText("title", "Название");
        AddText("description", "Описание");
        AddText("type", "Тип (kill/collect)");

        var monsterCol = new DataGridViewComboBoxColumn
        {
            DataPropertyName = "monster",
            HeaderText = "Монстр (цель)",
            Name = "monster",
            DataSource = _monsterRefs.Select(r => r.Name).ToList(),
            FlatStyle = FlatStyle.Flat
        };
        _questsGrid.Columns.Add(monsterCol);

        var itemCol = new DataGridViewComboBoxColumn
        {
            DataPropertyName = "item",
            HeaderText = "Предмет (цель)",
            Name = "item",
            DataSource = _collectibleRefs.Select(r => r.Name).ToList(),
            FlatStyle = FlatStyle.Flat
        };
        _questsGrid.Columns.Add(itemCol);

        AddText("target", "Кол-во");
        AddText("xp_reward", "Опыт");
        AddText("gold_reward", "Золото");
    }

    private static string NameById(List<(string Id, string Name)> refs, string id)
    {
        var found = refs.FirstOrDefault(r => r.Id == id);
        return found.Name ?? "";
    }

    private static string IdByName(List<(string Id, string Name)> refs, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var found = refs.FirstOrDefault(r => r.Name == name);
        return found.Id ?? "";
    }

    private DataTable LoadTable(string query)
    {
        var dt = new DataTable();
        using var conn = new SqliteConnection($"Data Source={_dbFile}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = query;
        using var reader = cmd.ExecuteReader();
        for (int i = 0; i < reader.FieldCount; i++)
            dt.Columns.Add(reader.GetName(i), typeof(string));
        while (reader.Read())
        {
            var values = new object[reader.FieldCount];
            for (int i = 0; i < reader.FieldCount; i++)
                values[i] = reader.IsDBNull(i) ? "" : reader.GetValue(i).ToString() ?? "";
            dt.Rows.Add(values);
        }
        return dt;
    }

    private void LoadItems()
    {
        _itemsGrid.DataSource = LoadTable(@"SELECT id, name, type, value, attack, defense, max_health_bonus, heal_amount, stock, description,
            bonus_strength, bonus_stamina, bonus_agility, bonus_cunning, bonus_wisdom, bonus_will, bonus_crit_chance, bonus_crit_damage, bonus_evade_chance, two_handed
            FROM items ORDER BY id");
        SetupItemsTypeColumn();
        ApplyItemTypeView();
    }

    // Делаем колонку type выпадающим списком с каноничными типами
    private void SetupItemsTypeColumn()
    {
        if (_itemsGrid.Columns["type"] is DataGridViewTextBoxColumn)
        {
            var idx = _itemsGrid.Columns["type"].Index;
            _itemsGrid.Columns.Remove("type");
            var combo = new DataGridViewComboBoxColumn
            {
                Name = "type",
                HeaderText = "Тип",
                DataPropertyName = "type",
                Items = { "weapon", "twohand", "shield", "helmet", "cloak", "chest", "legs", "boots", "glove_r", "glove_l", "necklace", "ring", "accessory", "consumable", "collectible", "trophy" },
            };
            _itemsGrid.Columns.Insert(idx, combo);
        }
    }

    // Показываем только те колонки, что относятся к выбранному типу предмета
    private void ApplyItemTypeView()
    {
        string selected = _itemTypeSelector?.SelectedItem?.ToString() ?? "все";

        // Фильтруем строки по типу (если не "все")
        if (_itemsGrid.DataSource is DataTable dt)
        {
            dt.DefaultView.RowFilter = selected == "все" ? "" : $"type = '{selected}'";
        }

        // Базовые колонки, видимые всегда
        var alwaysVisible = new HashSet<string> { "id", "name", "type", "value", "stock", "description", "two_handed" };

        // Колонки, относящиеся к типам
        var relevant = selected switch
        {
            "weapon" or "twohand" => new HashSet<string> { "attack", "bonus_strength", "bonus_agility", "bonus_crit_chance", "bonus_crit_damage", "bonus_evade_chance" },
            "armor" or "shield" or "helmet" or "cloak" or "chest" or "legs" or "boots" or "glove_r" or "glove_l" => new HashSet<string> { "defense", "max_health_bonus", "bonus_stamina", "bonus_will", "bonus_evade_chance" },
            "accessory" or "necklace" or "ring" => new HashSet<string> { "attack", "defense", "max_health_bonus",
                "bonus_strength", "bonus_stamina", "bonus_agility", "bonus_cunning", "bonus_wisdom", "bonus_will",
                "bonus_crit_chance", "bonus_crit_damage", "bonus_evade_chance" },
            "consumable" => new HashSet<string> { "heal_amount", "max_health_bonus" },
            "collectible" => new HashSet<string> { },
            "trophy" => new HashSet<string> { "attack", "defense", "max_health_bonus", "heal_amount",
                "bonus_strength", "bonus_stamina", "bonus_agility", "bonus_cunning", "bonus_wisdom", "bonus_will",
                "bonus_crit_chance", "bonus_crit_damage", "bonus_evade_chance" },
            _ => null
        };

        foreach (DataGridViewColumn col in _itemsGrid.Columns)
        {
            if (col.Name == "type") { col.Visible = true; continue; }
            if (relevant == null)
                col.Visible = true; // "все"
            else
                col.Visible = alwaysVisible.Contains(col.Name) || relevant.Contains(col.Name);
        }
    }

    private void LoadMonsters()
    {
        _monstersGrid.DataSource = LoadTable(@"SELECT id, name, tier, health, attack, defense, xp_reward, gold_reward, symbol,
            strength, stamina, agility, cunning, wisdom, will, crit_chance, crit_damage, evade_chance
            FROM monsters ORDER BY id");
    }

    private void SaveItems()
    {
        try
        {
            _itemsGrid.EndEdit();
            var dt = (DataTable)_itemsGrid.DataSource;
            EnsureId(dt, "I");
            using var conn = new SqliteConnection($"Data Source={_dbFile}");
            conn.Open();
            using var transaction = conn.BeginTransaction();
            // Простая стратегия: очищаем и вставляем заново (id — первичный ключ)
            using (var del = conn.CreateCommand()) { del.CommandText = "DELETE FROM items"; del.ExecuteNonQuery(); }
            foreach (DataRow row in dt.Rows)
            {
                if (row.RowState == DataRowState.Deleted) continue;
                if (string.IsNullOrWhiteSpace(row["id"]?.ToString())) continue;
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"INSERT INTO items (id, name, type, value, attack, defense, max_health_bonus, heal_amount, stock, description,
                        bonus_strength, bonus_stamina, bonus_agility, bonus_cunning, bonus_wisdom, bonus_will, bonus_crit_chance, bonus_crit_damage, bonus_evade_chance, two_handed)
                    VALUES ($id,$n,$t,$v,$a,$d,$m,$h,$s,$desc,$str,$sta,$agi,$cun,$wis,$wil,$cc,$cd,$ec,$th)";
                cmd.Parameters.AddWithValue("$id", row["id"]);
                cmd.Parameters.AddWithValue("$n", row["name"] ?? "");
                cmd.Parameters.AddWithValue("$t", row["type"] ?? "");
                cmd.Parameters.AddWithValue("$v", ToInt(row["value"]));
                cmd.Parameters.AddWithValue("$a", ToInt(row["attack"]));
                cmd.Parameters.AddWithValue("$d", ToInt(row["defense"]));
                cmd.Parameters.AddWithValue("$m", ToInt(row["max_health_bonus"]));
                cmd.Parameters.AddWithValue("$h", ToInt(row["heal_amount"]));
                cmd.Parameters.AddWithValue("$s", ToInt(row["stock"]));
                cmd.Parameters.AddWithValue("$desc", row["description"] ?? "");
                cmd.Parameters.AddWithValue("$str", ToInt(row["bonus_strength"]));
                cmd.Parameters.AddWithValue("$sta", ToInt(row["bonus_stamina"]));
                cmd.Parameters.AddWithValue("$agi", ToInt(row["bonus_agility"]));
                cmd.Parameters.AddWithValue("$cun", ToInt(row["bonus_cunning"]));
                cmd.Parameters.AddWithValue("$wis", ToInt(row["bonus_wisdom"]));
                cmd.Parameters.AddWithValue("$wil", ToInt(row["bonus_will"]));
                cmd.Parameters.AddWithValue("$cc", ToDouble(row["bonus_crit_chance"]));
                cmd.Parameters.AddWithValue("$cd", ToDouble(row["bonus_crit_damage"]));
                cmd.Parameters.AddWithValue("$ec", ToDouble(row["bonus_evade_chance"]));
                cmd.Parameters.AddWithValue("$th", ToInt(row["two_handed"]));
                cmd.ExecuteNonQuery();
            }
            transaction.Commit();
            LoadItems();
            SetStatus("Предметы сохранены");
        }
        catch (Exception ex)
        {
            SetStatus("Ошибка (предметы): " + ex.Message);
        }
    }

    private void SaveMonsters()
    {
        try
        {
            _monstersGrid.EndEdit();
            var dt = (DataTable)_monstersGrid.DataSource;
            EnsureId(dt, "M");
            using var conn = new SqliteConnection($"Data Source={_dbFile}");
            conn.Open();
            using var transaction = conn.BeginTransaction();
            using (var del = conn.CreateCommand()) { del.CommandText = "DELETE FROM monsters"; del.ExecuteNonQuery(); }
            foreach (DataRow row in dt.Rows)
            {
                if (row.RowState == DataRowState.Deleted) continue;
                if (string.IsNullOrWhiteSpace(row["id"]?.ToString())) continue;
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"INSERT INTO monsters (id, name, tier, health, attack, defense, xp_reward, gold_reward, symbol,
                        strength, stamina, agility, cunning, wisdom, will, crit_chance, crit_damage, evade_chance)
                    VALUES ($id,$n,$t,$hp,$a,$d,$xp,$g,$s,$str,$sta,$agi,$cun,$wis,$wil,$cc,$cd,$ec)";
                cmd.Parameters.AddWithValue("$id", row["id"]);
                cmd.Parameters.AddWithValue("$n", row["name"] ?? "");
                cmd.Parameters.AddWithValue("$t", ToInt(row["tier"]));
                cmd.Parameters.AddWithValue("$hp", ToInt(row["health"]));
                cmd.Parameters.AddWithValue("$a", ToInt(row["attack"]));
                cmd.Parameters.AddWithValue("$d", ToInt(row["defense"]));
                cmd.Parameters.AddWithValue("$xp", ToInt(row["xp_reward"]));
                cmd.Parameters.AddWithValue("$g", ToInt(row["gold_reward"]));
                cmd.Parameters.AddWithValue("$s", (row["symbol"]?.ToString() ?? "M").Length > 0 ? row["symbol"].ToString()[0].ToString() : "M");
                cmd.Parameters.AddWithValue("$str", ToInt(row["strength"]));
                cmd.Parameters.AddWithValue("$sta", ToInt(row["stamina"]));
                cmd.Parameters.AddWithValue("$agi", ToInt(row["agility"]));
                cmd.Parameters.AddWithValue("$cun", ToInt(row["cunning"]));
                cmd.Parameters.AddWithValue("$wis", ToInt(row["wisdom"]));
                cmd.Parameters.AddWithValue("$wil", ToInt(row["will"]));
                cmd.Parameters.AddWithValue("$cc", ToDouble(row["crit_chance"]));
                cmd.Parameters.AddWithValue("$cd", ToDouble(row["crit_damage"]));
                cmd.Parameters.AddWithValue("$ec", ToDouble(row["evade_chance"]));
                cmd.ExecuteNonQuery();
            }
            transaction.Commit();
            LoadMonsters();
            SetStatus("Монстры сохранены");
        }
        catch (Exception ex)
        {
            SetStatus("Ошибка (монстры): " + ex.Message);
        }
    }

    // Генерирует id для новых строк: префикс + следующий номер
    private void EnsureId(DataTable dt, string prefix)
    {
        int maxNum = 0;
        foreach (DataRow row in dt.Rows)
        {
            if (row.RowState == DataRowState.Deleted) continue;
            var id = row["id"]?.ToString() ?? "";
            if (id.StartsWith(prefix) && int.TryParse(id.Substring(prefix.Length), out int num))
                maxNum = Math.Max(maxNum, num);
        }
        foreach (DataRow row in dt.Rows)
        {
            if (row.RowState == DataRowState.Deleted) continue;
            if (string.IsNullOrWhiteSpace(row["id"]?.ToString()))
            {
                maxNum++;
                row["id"] = prefix + maxNum.ToString("D4");
            }
        }
    }

    private static int ToInt(object? v) => int.TryParse(v?.ToString(), out int r) ? r : 0;

    private static double ToDouble(object? v) => double.TryParse(v?.ToString(), System.Globalization.CultureInfo.InvariantCulture, out double r) ? r : 0;

    private static string CellStr(DataGridViewRow row, string col)
    {
        var val = row.Cells[col].Value;
        return val?.ToString() ?? "";
    }

    private class NpcRecord
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public int X { get; set; }
        public int Y { get; set; }
    }

    private void SaveNpcsLocal(List<NpcRecord> npcs)
    {
        using var conn = new SqliteConnection($"Data Source={_dbFile}");
        conn.Open();
        using var transaction = conn.BeginTransaction();
        using (var del = conn.CreateCommand())
        {
            del.CommandText = "DELETE FROM npcs";
            del.ExecuteNonQuery();
        }
        foreach (var n in npcs)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO npcs (id, name, type, x, y, data) VALUES ($id,$n,$t,$x,$y,NULL)";
            cmd.Parameters.AddWithValue("$id", n.Id);
            cmd.Parameters.AddWithValue("$n", n.Name);
            cmd.Parameters.AddWithValue("$t", n.Type);
            cmd.Parameters.AddWithValue("$x", n.X);
            cmd.Parameters.AddWithValue("$y", n.Y);
            cmd.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    private void SaveQuests()
    {
        try
        {
            _questsGrid.EndEdit();
            var dt = (DataTable)_questsGrid.DataSource;
            EnsureId(dt, "Q");
            using var conn = new SqliteConnection($"Data Source={_dbFile}");
            conn.Open();
            using var transaction = conn.BeginTransaction();
            using (var del = conn.CreateCommand()) { del.CommandText = "DELETE FROM quests_def"; del.ExecuteNonQuery(); }
            foreach (DataRow row in dt.Rows)
            {
                if (row.RowState == DataRowState.Deleted) continue;
                if (string.IsNullOrWhiteSpace(row["id"]?.ToString())) continue;
                string type = row["type"]?.ToString() ?? "kill";
                string monsterName = row["monster"]?.ToString() ?? "";
                string itemName = row["item"]?.ToString() ?? "";
                // Для kill цель — монстр, для collect — предмет
                string monsterId = type == "kill" ? IdByName(_monsterRefs, monsterName) : "";
                string itemId = type == "collect" ? IdByName(_collectibleRefs, itemName) : "";
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"INSERT INTO quests_def (id, title, description, type, target_monster_id, target_item_id, target, xp_reward, gold_reward)
                    VALUES ($id,$t,$d,$ty,$tm,$ti,$tg,$xp,$g)";
                cmd.Parameters.AddWithValue("$id", row["id"]);
                cmd.Parameters.AddWithValue("$t", row["title"] ?? "");
                cmd.Parameters.AddWithValue("$d", row["description"] ?? "");
                cmd.Parameters.AddWithValue("$ty", type);
                cmd.Parameters.AddWithValue("$tm", monsterId);
                cmd.Parameters.AddWithValue("$ti", itemId);
                cmd.Parameters.AddWithValue("$tg", ToInt(row["target"]));
                cmd.Parameters.AddWithValue("$xp", ToInt(row["xp_reward"]));
                cmd.Parameters.AddWithValue("$g", ToInt(row["gold_reward"]));
                cmd.ExecuteNonQuery();
            }
            transaction.Commit();
            LoadQuests();
            SetStatus("Квесты сохранены");
        }
        catch (Exception ex)
        {
            SetStatus("Ошибка (квесты): " + ex.Message);
        }
    }

    private void LoadWorld()
    {
        BuildWorldGrid();

        var dt = new DataTable();
        dt.Columns.Add("id", typeof(string));
        dt.Columns.Add("name", typeof(string));
        dt.Columns.Add("type", typeof(string));
        dt.Columns.Add("x", typeof(string));
        dt.Columns.Add("y", typeof(string));

        using var conn = new SqliteConnection($"Data Source={_dbFile}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, type, x, y FROM npcs ORDER BY id";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            dt.Rows.Add(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3).ToString(),
                reader.GetInt32(4).ToString());
        }
        _worldGrid.DataSource = dt;

        // Размер карты — из world_config
        _worldWidth = GetWorldConfigInt("width", 100);
        _worldHeight = GetWorldConfigInt("height", 100);
    }

    private void BuildWorldGrid()
    {
        _worldGrid.Columns.Clear();
        _worldGrid.DataSource = null;

        var typeCol = new DataGridViewComboBoxColumn
        {
            Name = "type",
            HeaderText = "Тип",
            DataPropertyName = "type",
            Items = { "merchant", "board" },
        };

        var idCol = new DataGridViewTextBoxColumn { Name = "id", HeaderText = "ID", DataPropertyName = "id", ReadOnly = true };
        var nameCol = new DataGridViewTextBoxColumn { Name = "name", HeaderText = "Имя", DataPropertyName = "name" };
        var xCol = new DataGridViewTextBoxColumn { Name = "x", HeaderText = "X", DataPropertyName = "x" };
        var yCol = new DataGridViewTextBoxColumn { Name = "y", HeaderText = "Y", DataPropertyName = "y" };

        _worldGrid.Columns.Add(idCol);
        _worldGrid.Columns.Add(nameCol);
        _worldGrid.Columns.Add(typeCol);
        _worldGrid.Columns.Add(xCol);
        _worldGrid.Columns.Add(yCol);
    }

    private int GetWorldConfigInt(string key, int defaultValue)
    {
        try
        {
            using var conn = new SqliteConnection($"Data Source={_dbFile}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT value FROM world_config WHERE key = $k";
            cmd.Parameters.AddWithValue("$k", key);
            var v = cmd.ExecuteScalar();
            return v == null ? defaultValue : Convert.ToInt32(v);
        }
        catch { return defaultValue; }
    }

    private void SaveWorld()
    {
        try
        {
            _worldGrid.EndEdit();

            // Сохраняем NPC
            var npcs = new List<NpcRecord>();
            int maxNum = 0;
            foreach (DataGridViewRow row in _worldGrid.Rows)
            {
                if (row.IsNewRow) continue;
                string id = CellStr(row, "id");
                string name = CellStr(row, "name");
                string type = CellStr(row, "type");
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(type)) continue;
                if (string.IsNullOrWhiteSpace(id))
                {
                    id = "N" + (maxNum + 1).ToString("D4");
                }
                if (id.StartsWith("N") && int.TryParse(id.Substring(1), out int n))
                {
                    if (n > maxNum) maxNum = n;
                }
                npcs.Add(new NpcRecord
                {
                    Id = id,
                    Name = name,
                    Type = type,
                    X = ToInt(CellStr(row, "x")),
                    Y = ToInt(CellStr(row, "y")),
                });
            }
            SaveNpcsLocal(npcs);

            // Сохраняем размер карты
            using (var conn = new SqliteConnection($"Data Source={_dbFile}"))
            {
                conn.Open();
                foreach (var kvp in new[] { ("width", _worldWidth), ("height", _worldHeight) })
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "UPDATE world_config SET value = $v WHERE key = $k";
                    cmd.Parameters.AddWithValue("$k", kvp.Item1);
                    cmd.Parameters.AddWithValue("$v", kvp.Item2);
                    cmd.ExecuteNonQuery();
                }
            }

            LoadWorld();
            SetStatus("NPC и мир сохранены");
        }
        catch (Exception ex)
        {
            SetStatus("Ошибка (мир): " + ex.Message);
        }
    }

    private void SetStatus(string text) => _status.Text = text;

    // ===== Ассортимент торговца =====
    private void LoadMerchantStockEditor()
    {
        try
        {
            _merchantStockList.Items.Clear();

            // Все предметы, кроме собираемых (те не продаются)
            var items = new List<(string Id, string Name)>();
            using (var conn = new SqliteConnection($"Data Source={_dbFile}"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT id, name, type FROM items WHERE type <> 'collectible' ORDER BY id";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                    items.Add((reader.GetString(0), reader.GetString(1)));
            }

            // Текущий ассортимент торговца
            var merchantId = GetMerchantNpcId();
            var stock = new HashSet<string>();
            if (merchantId != null)
            {
                using var conn = new SqliteConnection($"Data Source={_dbFile}");
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT item_id FROM merchant_stock WHERE npc_id = $npc";
                cmd.Parameters.AddWithValue("$npc", merchantId);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                    stock.Add(reader.GetString(0));
            }

            foreach (var (id, name) in items)
            {
                int idx = _merchantStockList.Items.Add($"{id}  —  {name}", stock.Contains(id));
            }

            SetStatus("Ассортимент торговца загружен");
        }
        catch (Exception ex)
        {
            SetStatus("Ошибка (ассортимент): " + ex.Message);
        }
    }

    private string? GetMerchantNpcId()
    {
        using var conn = new SqliteConnection($"Data Source={_dbFile}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id FROM npcs WHERE type = 'merchant' LIMIT 1";
        var v = cmd.ExecuteScalar();
        return v?.ToString();
    }

    private void SaveMerchantStockEditor()
    {
        try
        {
            var merchantId = GetMerchantNpcId();
            if (merchantId == null)
            {
                SetStatus("Нет NPC типа 'merchant' — сначала создайте торговца во вкладке 'Мир'");
                return;
            }

            var selected = new List<string>();
            foreach (var item in _merchantStockList.CheckedItems)
            {
                var text = item?.ToString() ?? "";
                int sep = text.IndexOf("  —  ");
                if (sep > 0) selected.Add(text.Substring(0, sep).Trim());
            }

            using var conn = new SqliteConnection($"Data Source={_dbFile}");
            conn.Open();
            using var transaction = conn.BeginTransaction();
            using (var del = conn.CreateCommand())
            {
                del.CommandText = "DELETE FROM merchant_stock WHERE npc_id = $npc";
                del.Parameters.AddWithValue("$npc", merchantId);
                del.ExecuteNonQuery();
            }
            foreach (var itemId in selected)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT OR IGNORE INTO merchant_stock (npc_id, item_id) VALUES ($npc, $item)";
                cmd.Parameters.AddWithValue("$npc", merchantId);
                cmd.Parameters.AddWithValue("$item", itemId);
                cmd.ExecuteNonQuery();
            }
            transaction.Commit();

            LoadMerchantStockEditor();
            SetStatus($"Ассортимент сохранён: {selected.Count} предметов");
        }
        catch (Exception ex)
        {
            SetStatus("Ошибка (ассортимент): " + ex.Message);
        }
    }

    private void LoadLoot()
    {
        var dt = new DataTable();
        dt.Columns.Add("id", typeof(int));
        dt.Columns.Add("monster_id", typeof(string));
        dt.Columns.Add("name", typeof(string));
        dt.Columns.Add("description", typeof(string));
        dt.Columns.Add("value", typeof(int));
        dt.Columns.Add("drop_chance", typeof(int));

        using var conn = new SqliteConnection($"Data Source={_dbFile}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, monster_id, name, description, value, drop_chance FROM loot_tables ORDER BY monster_id, id";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            dt.Rows.Add(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt32(4), reader.GetInt32(5));
        }

        _lootGrid.DataSource = dt;
        if (_lootGrid.Columns.Contains("monster_id"))
        {
            var monsterCol = _lootGrid.Columns["monster_id"]!;
            var monsterNames = _monsterRefs.ToDictionary(r => r.Id, r => r.Name);
            var comboCol = new DataGridViewComboBoxColumn
            {
                Name = "monster_id",
                HeaderText = "monster_id",
                DataSource = _monsterRefs.Select(r => r.Id).ToList(),
                FlatStyle = FlatStyle.Flat,
                DisplayStyle = DataGridViewComboBoxDisplayStyle.ComboBox,
                FillWeight = 80,
            };
            int idx = _lootGrid.Columns.IndexOf(monsterCol);
            _lootGrid.Columns.Remove(monsterCol);
            _lootGrid.Columns.Insert(idx, comboCol);
        }
    }

    private void SaveLoot()
    {
        try
        {
            _lootGrid.EndEdit();
            var dt = (DataTable)_lootGrid.DataSource!;
            using var conn = new SqliteConnection($"Data Source={_dbFile}");
            conn.Open();
            using var transaction = conn.BeginTransaction();
            using (var del = conn.CreateCommand()) { del.CommandText = "DELETE FROM loot_tables"; del.ExecuteNonQuery(); }
            foreach (DataRow row in dt.Rows)
            {
                if (row.RowState == DataRowState.Deleted) continue;
                var monsterId = row["monster_id"]?.ToString() ?? "";
                var name = row["name"]?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(monsterId) || string.IsNullOrWhiteSpace(name)) continue;
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"INSERT INTO loot_tables (monster_id, name, description, value, drop_chance)
                    VALUES ($mid, $n, $d, $v, $dc)";
                cmd.Parameters.AddWithValue("$mid", monsterId);
                cmd.Parameters.AddWithValue("$n", name);
                cmd.Parameters.AddWithValue("$d", row["description"] ?? "");
                cmd.Parameters.AddWithValue("$v", ToInt(row["value"]));
                cmd.Parameters.AddWithValue("$dc", ToInt(row["drop_chance"]));
                cmd.ExecuteNonQuery();
            }
            transaction.Commit();
            LoadLoot();
            SetStatus("Лут сохранён");
        }
        catch (Exception ex)
        {
            SetStatus("Ошибка (лут): " + ex.Message);
        }
    }

    // ===== Анимации (спрайт-листы) =====
    private sealed class AnimEntry
    {
        public string Key { get; set; } = "";
        public string Sheet { get; set; } = "";
        public int Cols { get; set; } = 1;
        public int Rows { get; set; } = 1;
        public int Fps { get; set; } = 8;
    }

    private string ClientBinContent()
    {
        var solRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        return Path.Combine(solRoot, "RPGO.ClientMonoGame", "bin", "Debug", "net8.0", "Content");
    }

    private string ClientSrcContent()
    {
        var solRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        return Path.Combine(solRoot, "RPGO.ClientMonoGame", "Content");
    }

    private string? ResolveSheetPath(string sheet)
    {
        if (_animSrcPaths.TryGetValue(sheet, out var src) && File.Exists(src)) return src;
        string binPath = Path.Combine(ClientBinContent(), "Animations", sheet);
        if (File.Exists(binPath)) return binPath;
        string srcPath = Path.Combine(ClientSrcContent(), "Animations", sheet);
        if (File.Exists(srcPath)) return srcPath;
        return null;
    }

    private TabPage BuildAnimationsTab()
    {
        var tab = new TabPage("Анимации");
        var panel = new Panel { Dock = DockStyle.Fill };

        var top = new Panel { Dock = DockStyle.Top, Height = 36, Padding = new Padding(6, 4, 6, 4) };
        _animAddBtn = new Button { Text = "Добавить…", Dock = DockStyle.Left, Width = 110 };
        _animAddBtn.Click += (s, e) => AddAnimation();
        _animDelBtn = new Button { Text = "Удалить", Dock = DockStyle.Left, Width = 90 };
        _animDelBtn.Click += (s, e) => DeleteAnimation();
        _animSaveBtn = new Button { Text = "Сохранить анимации", Dock = DockStyle.Right, Width = 160 };
        _animSaveBtn.Click += (s, e) => SaveAnimations();
        top.Controls.Add(_animSaveBtn);
        top.Controls.Add(_animDelBtn);
        top.Controls.Add(_animAddBtn);

        _animGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect
        };
        _animGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "key", HeaderText = "Ключ (entity)", DataPropertyName = "key" });
        _animGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "sheet", HeaderText = "Файл спрайт-листа", DataPropertyName = "sheet", ReadOnly = true });
        _animGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "cols", HeaderText = "Колонки", DataPropertyName = "cols" });
        _animGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "rows", HeaderText = "Строки", DataPropertyName = "rows" });
        _animGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "fps", HeaderText = "Кадров/сек", DataPropertyName = "fps" });
        _animGrid.SelectionChanged += (s, e) => UpdateAnimPreview();
        _animGrid.CellValueChanged += (s, e) => UpdateAnimPreview();

        _animPreview = new PictureBox
        {
            Dock = DockStyle.Right,
            Width = 170,
            BackColor = System.Drawing.Color.FromArgb(30, 30, 35),
            SizeMode = PictureBoxSizeMode.CenterImage
        };

        var leftPanel = new Panel { Dock = DockStyle.Fill };
        leftPanel.Controls.Add(_animGrid);

        var split = new Panel { Dock = DockStyle.Fill };
        split.Controls.Add(_animPreview);
        split.Controls.Add(leftPanel);

        panel.Controls.Add(split);
        panel.Controls.Add(top);
        tab.Controls.Add(panel);

        _animTimer = new System.Windows.Forms.Timer { Interval = 100 };
        _animTimer.Tick += (s, e) => DrawAnimPreviewFrame();
        _animStopwatch.Restart();
        _animTimer.Start();

        LoadAnimationsGrid();
        return tab;
    }

    private void LoadAnimationsGrid()
    {
        _animGrid.Rows.Clear();
        _animSrcPaths.Clear();
        string jsonPath = Path.Combine(ClientBinContent(), "animations.json");
        if (!File.Exists(jsonPath)) return;
        try
        {
            var entries = JsonSerializer.Deserialize<List<AnimEntry>>(File.ReadAllText(jsonPath));
            if (entries == null) return;
            foreach (var e in entries)
                _animGrid.Rows.Add(e.Key, e.Sheet, e.Cols, e.Rows, e.Fps);
        }
        catch (Exception ex)
        {
            SetStatus("Ошибка чтения animations.json: " + ex.Message);
        }
    }

    private void AddAnimation()
    {
        using var dlg = new OpenFileDialog
        {
            Filter = "PNG спрайт-лист|*.png",
            Title = "Выберите PNG спрайт-лист"
        };
        if (dlg.ShowDialog() != DialogResult.OK) return;

        string path = dlg.FileName;
        string fileName = Path.GetFileName(path);
        string key = Path.GetFileNameWithoutExtension(path);

        int cols = 4, rows = 1, fps = 8;
        try
        {
            using var img = System.Drawing.Image.FromFile(path);
            int guess = (int)Math.Round((double)img.Width / Math.Max(1, img.Height));
            if (guess >= 1) cols = guess;
        }
        catch { }

        foreach (DataGridViewRow r in _animGrid.Rows)
        {
            if (r.Cells["key"].Value?.ToString() == key)
            {
                r.Cells["sheet"].Value = fileName;
                r.Cells["cols"].Value = cols;
                r.Cells["rows"].Value = rows;
                r.Cells["fps"].Value = fps;
                _animSrcPaths[fileName] = path;
                UpdateAnimPreview();
                SetStatus($"Анимация '{key}' обновлена");
                return;
            }
        }

        _animGrid.Rows.Add(key, fileName, cols, rows, fps);
        _animSrcPaths[fileName] = path;
        UpdateAnimPreview();
        SetStatus($"Анимация '{key}' добавлена (отредактируйте колонки/строки/кадры в таблице)");
    }

    private void DeleteAnimation()
    {
        if (_animGrid.SelectedRows.Count == 0) return;
        var row = _animGrid.SelectedRows[0];
        string? sheet = row.Cells["sheet"].Value?.ToString();
        _animGrid.Rows.Remove(row);
        if (sheet != null && _animSrcPaths.ContainsKey(sheet)) _animSrcPaths.Remove(sheet);
        UpdateAnimPreview();
    }

    private void UpdateAnimPreview()
    {
        _animPreviewImage?.Dispose();
        _animPreviewImage = null;
        _animPreview.Image = null;
        if (_animGrid.SelectedRows.Count == 0) return;
        var row = _animGrid.SelectedRows[0];
        string sheet = row.Cells["sheet"].Value?.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(sheet)) return;
        string? path = ResolveSheetPath(sheet);
        if (path == null || !File.Exists(path)) return;
        try { _animPreviewImage = System.Drawing.Image.FromFile(path); }
        catch { _animPreviewImage = null; }
        _animStopwatch.Restart();
    }

    private void DrawAnimPreviewFrame()
    {
        if (_animPreviewImage == null || _animGrid.SelectedRows.Count == 0) return;
        var row = _animGrid.SelectedRows[0];
        int cols = Math.Max(1, ToInt(row.Cells["cols"].Value));
        int rows = Math.Max(1, ToInt(row.Cells["rows"].Value));
        int fps = Math.Max(1, ToInt(row.Cells["fps"].Value));
        int fw = _animPreviewImage.Width / cols;
        int fh = _animPreviewImage.Height / rows;
        int total = cols * rows;
        int frame = (int)(_animStopwatch.Elapsed.TotalSeconds * fps) % total;
        int c = frame % cols;
        int r = frame / cols;
        var src = new System.Drawing.Rectangle(c * fw, r * fh, fw, fh);

        int targetW = Math.Max(1, Math.Min(_animPreview.Width - 20, fw * 3));
        int targetH = (int)((double)targetW / fw * fh);
        if (targetH < 1) targetH = 1;
        var bmp = new System.Drawing.Bitmap(targetW, targetH);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
            g.DrawImage(_animPreviewImage, new System.Drawing.Rectangle(0, 0, targetW, targetH), src, System.Drawing.GraphicsUnit.Pixel);
        }
        var old = _animPreview.Image;
        _animPreview.Image = bmp;
        old?.Dispose();
    }

    private void SaveAnimations()
    {
        try
        {
            var entries = new List<AnimEntry>();
            foreach (DataGridViewRow row in _animGrid.Rows)
            {
                if (row.IsNewRow) continue;
                string key = row.Cells["key"].Value?.ToString() ?? "";
                string sheet = row.Cells["sheet"].Value?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(sheet)) continue;
                entries.Add(new AnimEntry
                {
                    Key = key,
                    Sheet = sheet,
                    Cols = Math.Max(1, ToInt(row.Cells["cols"].Value)),
                    Rows = Math.Max(1, ToInt(row.Cells["rows"].Value)),
                    Fps = Math.Max(1, ToInt(row.Cells["fps"].Value))
                });
            }

            string json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
            foreach (var content in new[] { ClientBinContent(), ClientSrcContent() })
            {
                Directory.CreateDirectory(content);
                Directory.CreateDirectory(Path.Combine(content, "Animations"));
                File.WriteAllText(Path.Combine(content, "animations.json"), json);
                foreach (var e in entries)
                {
                    if (_animSrcPaths.TryGetValue(e.Sheet, out var src) && File.Exists(src))
                        File.Copy(src, Path.Combine(content, "Animations", e.Sheet), true);
                }
            }
            SetStatus($"Анимации сохранены: {entries.Count} (записано в bin и исходники клиента)");
        }
        catch (Exception ex)
        {
            SetStatus("Ошибка (анимации): " + ex.Message);
        }
    }
}

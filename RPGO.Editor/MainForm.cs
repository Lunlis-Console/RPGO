using System.Data;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using RPGGame.Shared.Migrations;
using RPGGame.Shared.Models;

namespace RPGGame.Editor;

public partial class MainForm : Form
{
    private readonly string _dbFile;
    private TabControl _tabs = null!;
    private TabPage _itemsTab = null!;
    private TabPage _monstersTab = null!;
    private TabPage _lootTab = null!;
    private TabPage _questsTab = null!;
    private TabPage _worldTab = null!;
    private TabPage _merchantTab = null!;
    private TabPage _animTab = null!;
    private TabPage _accountsTab = null!;

    private DataGridView _itemsGrid = null!;
    private DataGridView _monstersGrid = null!;
    private DataGridView _questsGrid = null!;
    private DataGridView _worldGrid = null!;
    private DataGridView _lootGrid = null!;
    private Label _status = null!;

    private List<(string Id, string Name)> _monsterRefs = new();
    private List<(string Id, string Name)> _collectibleRefs = new();
    private List<(string Id, string Name)> _npcRefs = new();
    private List<(string Id, string Name)> _questRefs = new();
    private List<(string Id, string Name)> _rewardItemRefs = new();
    private int _worldWidth = 100;
    private int _worldHeight = 100;
    private ComboBox _itemTypeSelector = null!;
    private CheckedListBox _merchantStockList = null!;
    private TextBox _merchantSearch = null!;
    private ComboBox _merchantCategoryFilter = null!;

    // Search boxes per tab
    private TextBox _itemsSearch = null!;
    private TextBox _monstersSearch = null!;
    private TextBox _lootSearch = null!;
    private TextBox _questsSearch = null!;

    // Animations
    private DataGridView _animGrid = null!;
    private PictureBox _animPreview = null!;
    private System.Windows.Forms.Timer _animTimer = null!;
    private Button _animAddBtn = null!;
    private Button _animDelBtn = null!;
    private Button _animSaveBtn = null!;
    private readonly Dictionary<string, string> _animSrcPaths = new();
    private System.Drawing.Image? _animPreviewImage;
    private readonly Stopwatch _animStopwatch = new();

    // Colors (system theme)

    public MainForm(string dbFile)
    {
        _dbFile = dbFile;
        Text = "Редактор RPGO — " + Path.GetFileName(dbFile);
        Size = new Size(1200, 720);
        MinimumSize = new Size(900, 500);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        KeyDown += (s, e) =>
        {
            if (e.Control && e.KeyCode == Keys.S)
            {
                SaveCurrentTab();
                e.Handled = true;
            }
        };
        InitializeUI();
        LoadAll();
    }

    private void InitializeUI()
    {
        _tabs = new TabControl { Dock = DockStyle.Fill };

        // --- Предметы ---
        _itemsTab = new TabPage("Предметы");
        var itemsPanel = new Panel { Dock = DockStyle.Fill };

        var itemsTop = new Panel { Dock = DockStyle.Top, Height = 64, Padding = new Padding(6) };
        var itemsSearchRow = new Panel { Dock = DockStyle.Top, Height = 28, Padding = new Padding(6, 2, 6, 2) };
        _itemsSearch = MakeSearchBox("Поиск предметов...");
        _itemsSearch.TextChanged += (s, e) => ApplyItemsFilter();
        itemsSearchRow.Controls.Add(_itemsSearch);

        var itemsTypeRow = new Panel { Dock = DockStyle.Top, Height = 32, Padding = new Padding(6, 4, 6, 4) };
        var typeLabel = new Label { Text = "Тип:", Dock = DockStyle.Left, Width = 35, TextAlign = ContentAlignment.MiddleLeft };
        _itemTypeSelector = new ComboBox
        {
            Dock = DockStyle.Left,
            Width = 160,
            DropDownStyle = ComboBoxStyle.DropDownList};
        _itemTypeSelector.Items.AddRange(new object[] { "все", "weapon", "twohand", "shield", "helmet", "cloak", "chest", "legs", "boots", "glove", "belt", "necklace", "ring", "accessory", "consumable", "collectible", "trophy" });
        _itemTypeSelector.SelectedIndex = 0;
        _itemTypeSelector.SelectedIndexChanged += (s, e) => ApplyItemTypeView();
        itemsTypeRow.Controls.Add(_itemTypeSelector);
        itemsTypeRow.Controls.Add(typeLabel);

        itemsTop.Controls.Add(itemsTypeRow);
        itemsTop.Controls.Add(itemsSearchRow);

        _itemsGrid = MakeGrid();
        _itemsGrid.AllowUserToAddRows = true;
        _itemsGrid.AllowUserToDeleteRows = true;
        _itemsGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _itemsGrid.CellDoubleClick += (s, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= _itemsGrid.Rows.Count) return;
            if (_itemsGrid.Rows[e.RowIndex].IsNewRow) return;
            EditItemRow(_itemsGrid.Rows[e.RowIndex]);
        };
        itemsPanel.Controls.Add(_itemsGrid);
        itemsPanel.Controls.Add(itemsTop);
        var itemsBtn = MakeSaveButton("Сохранить предметы");
        itemsBtn.Click += (s, e) => SaveItems();
        itemsPanel.Controls.Add(itemsBtn);
        _itemsTab.Controls.Add(itemsPanel);

        // --- Монстры ---
        _monstersTab = new TabPage("Монстры");
        var monstersPanel = new Panel { Dock = DockStyle.Fill };
        var monstersTop = new Panel { Dock = DockStyle.Top, Height = 32, Padding = new Padding(6, 4, 6, 4) };
        _monstersSearch = MakeSearchBox("Поиск монстров...");
        _monstersSearch.TextChanged += (s, e) => ApplyGridFilter(_monstersGrid, _monstersSearch.Text);
        monstersTop.Controls.Add(_monstersSearch);
        monstersPanel.Controls.Add(_monstersGrid = MakeGrid());
        monstersPanel.Controls.Add(monstersTop);
        var monstersBtn = MakeSaveButton("Сохранить монстров");
        monstersBtn.Click += (s, e) => SaveMonsters();
        monstersPanel.Controls.Add(monstersBtn);
        _monstersTab.Controls.Add(monstersPanel);

        // --- Лут ---
        _lootTab = new TabPage("Лут");
        var lootPanel = new Panel { Dock = DockStyle.Fill };
        var lootTop = new Panel { Dock = DockStyle.Top, Height = 32, Padding = new Padding(6, 4, 6, 4) };
        _lootSearch = MakeSearchBox("Поиск лута...");
        _lootSearch.TextChanged += (s, e) => ApplyGridFilter(_lootGrid, _lootSearch.Text);
        lootTop.Controls.Add(_lootSearch);
        lootPanel.Controls.Add(_lootGrid = MakeGrid());
        lootPanel.Controls.Add(lootTop);
        var lootBtn = MakeSaveButton("Сохранить лут");
        lootBtn.Click += (s, e) => SaveLoot();
        lootPanel.Controls.Add(lootBtn);
        _lootTab.Controls.Add(lootPanel);

        // --- Квесты ---
        _questsTab = new TabPage("Квесты");
        var questsPanel = new Panel { Dock = DockStyle.Fill };
        var questsTop = new Panel { Dock = DockStyle.Top, Height = 32, Padding = new Padding(6, 4, 6, 4) };
        _questsSearch = MakeSearchBox("Поиск квестов...");
        _questsSearch.TextChanged += (s, e) => ApplyGridFilter(_questsGrid, _questsSearch.Text);
        questsTop.Controls.Add(_questsSearch);
        questsPanel.Controls.Add(_questsGrid = MakeGrid());
        questsPanel.Controls.Add(questsTop);
        var questsBtn = MakeSaveButton("Сохранить квесты");
        questsBtn.Click += (s, e) => SaveQuests();
        questsPanel.Controls.Add(questsBtn);
        _questsTab.Controls.Add(questsPanel);

        // --- Мир (NPC + размер карты) ---
        _worldTab = new TabPage("Мир");
        var worldPanel = new Panel { Dock = DockStyle.Fill };
        _worldGrid = MakeGrid();
        _worldGrid.AllowUserToAddRows = true;
        _worldGrid.AllowUserToDeleteRows = true;
        worldPanel.Controls.Add(_worldGrid);
        var dialogBtn = MakeSaveButton("Редактор диалогов NPC...");
        dialogBtn.Click += (s, e) => OpenDialogueEditor();
        worldPanel.Controls.Add(dialogBtn);
        var worldBtn = MakeSaveButton("Сохранить NPC и мир");
        worldBtn.Click += (s, e) => SaveWorld();
        worldPanel.Controls.Add(worldBtn);
        _worldTab.Controls.Add(worldPanel);

        // --- Торговец ---
        _merchantTab = new TabPage("Торговец");
        var merchantPanel = new Panel { Dock = DockStyle.Fill };

        var merchantTop = new Panel { Dock = DockStyle.Top, Height = 64, Padding = new Padding(6) };
        var merchantSearchRow = new Panel { Dock = DockStyle.Top, Height = 28, Padding = new Padding(6, 2, 6, 2) };
        _merchantSearch = MakeSearchBox("Поиск предметов...");
        _merchantSearch.TextChanged += (s, e) => ApplyMerchantFilter();
        merchantSearchRow.Controls.Add(_merchantSearch);

        var merchantBtnRow = new Panel { Dock = DockStyle.Top, Height = 32, Padding = new Padding(6, 4, 6, 4) };
        var selectAllBtn = MakeSmallButton("Выбрать все", SystemColors.ControlDark);
        selectAllBtn.Click += (s, e) => { for (int i = 0; i < _merchantStockList.Items.Count; i++) _merchantStockList.SetItemChecked(i, true); };
        var selectNoneBtn = MakeSmallButton("Снять все", SystemColors.ControlDarkDark);
        selectNoneBtn.Click += (s, e) => { for (int i = 0; i < _merchantStockList.Items.Count; i++) _merchantStockList.SetItemChecked(i, false); };
        var catLabel = new Label { Text = "Категория:", Dock = DockStyle.Left, Width = 75, TextAlign = ContentAlignment.MiddleLeft };
        _merchantCategoryFilter = new ComboBox
        {
            Dock = DockStyle.Left,
            Width = 130,
            DropDownStyle = ComboBoxStyle.DropDownList};
        _merchantCategoryFilter.Items.AddRange(new object[] { "все", "Оружие", "Доспехи", "Расходники", "Другое" });
        _merchantCategoryFilter.SelectedIndex = 0;
        _merchantCategoryFilter.SelectedIndexChanged += (s, e) => ApplyMerchantFilter();
        merchantBtnRow.Controls.Add(selectNoneBtn);
        merchantBtnRow.Controls.Add(selectAllBtn);
        merchantBtnRow.Controls.Add(_merchantCategoryFilter);
        merchantBtnRow.Controls.Add(catLabel);

        merchantTop.Controls.Add(merchantBtnRow);
        merchantTop.Controls.Add(merchantSearchRow);

        _merchantStockList = new CheckedListBox
        {
            Dock = DockStyle.Fill,
            CheckOnClick = true,
            Font = new Font("Segoe UI", 10),
            BorderStyle = BorderStyle.FixedSingle};

        merchantPanel.Controls.Add(_merchantStockList);
        merchantPanel.Controls.Add(merchantTop);
        var merchantBtn = MakeSaveButton("Сохранить ассортимент");
        merchantBtn.Click += (s, e) => SaveMerchantStockEditor();
        merchantPanel.Controls.Add(merchantBtn);
        _merchantTab.Controls.Add(merchantPanel);

        _tabs.TabPages.Add(_accountsTab = BuildAccountsTab());
        _tabs.TabPages.Add(_itemsTab);
        _tabs.TabPages.Add(_monstersTab);
        _tabs.TabPages.Add(_lootTab);
        _tabs.TabPages.Add(_questsTab);
        _tabs.TabPages.Add(_worldTab);
        _tabs.TabPages.Add(_merchantTab);
        _tabs.TabPages.Add(BuildAnimationsTab());
        _animTab = _tabs.TabPages[^1];

        Controls.Add(_tabs);

        _status = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 24,
            Text = "Готово",
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 0, 0)};
        Controls.Add(_status);
    }

    private TextBox MakeSearchBox(string placeholder)
    {
        var tb = new TextBox { Dock = DockStyle.Fill };
        tb.GotFocus += (s, e) => { if (tb.Text == placeholder) { tb.Text = ""; tb.ForeColor = SystemColors.WindowText; } };
        tb.LostFocus += (s, e) => { if (string.IsNullOrWhiteSpace(tb.Text)) { tb.Text = placeholder; tb.ForeColor = SystemColors.GrayText; } };
        tb.Text = placeholder;
        tb.ForeColor = SystemColors.GrayText;
        return tb;
    }

    private Button MakeSaveButton(string text)
    {
        return new Button
        {
            Text = text,
            Dock = DockStyle.Bottom,
            Height = 34,
            ForeColor = SystemColors.ControlText,
            FlatStyle = FlatStyle.Standard,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
    }

    private Button MakeSmallButton(string text, System.Drawing.Color bg)
    {
        return new Button
        {
            Text = text,
            Dock = DockStyle.Left,
            Width = 100,
            Height = 26,
            BackColor = bg,
            ForeColor = SystemColors.ControlText,
            FlatStyle = FlatStyle.Standard,
            Font = new Font("Segoe UI", 8f),
            Cursor = Cursors.Hand
        };
    }

    private DataGridView MakeGrid()
    {
        return new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = true,
            AllowUserToDeleteRows = true,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            RowHeadersWidth = 30,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect};
    }

    // === FILTER METHODS ===

    private void ApplyItemsFilter()
    {
        if (_itemsGrid.DataSource is not DataTable dt) return;
        string search = GetSearchText(_itemsSearch);
        string type = _itemTypeSelector?.SelectedItem?.ToString() ?? "все";
        string filter = "";
        if (type != "все") filter = $"type = '{type}'";
        if (!string.IsNullOrWhiteSpace(search))
        {
            string sFilter = $"(name LIKE '%{search}%' OR id LIKE '%{search}%')";
            filter = string.IsNullOrEmpty(filter) ? sFilter : $"({filter}) AND {sFilter}";
        }
        dt.DefaultView.RowFilter = filter;
    }

    private void ApplyGridFilter(DataGridView grid, string searchText)
    {
        if (grid.DataSource is not DataTable dt) return;
        string search = GetSearchText(searchText);
        if (string.IsNullOrWhiteSpace(search)) { dt.DefaultView.RowFilter = ""; return; }
        var parts = new List<string>();
        foreach (DataColumn col in dt.Columns)
        {
            if (col.DataType == typeof(string))
                parts.Add($"{col.ColumnName} LIKE '%{search}%'");
        }
        dt.DefaultView.RowFilter = string.Join(" OR ", parts);
    }

    private string GetSearchText(TextBox tb)
    {
        string text = tb.Text ?? "";
        // Ignore placeholder
        if (text.StartsWith("Поиск") || text.StartsWith("Пошук")) return "";
        return text;
    }

    private string GetSearchText(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText)) return "";
        if (rawText.StartsWith("Поиск") || rawText.StartsWith("Пошук")) return "";
        return rawText;
    }

    private void ApplyMerchantFilter()
    {
        string search = GetSearchText(_merchantSearch);
        string cat = _merchantCategoryFilter?.SelectedItem?.ToString() ?? "все";

        for (int i = 0; i < _merchantStockList.Items.Count; i++)
        {
            string text = _merchantStockList.Items[i]?.ToString() ?? "";
            bool matchSearch = string.IsNullOrWhiteSpace(search) || text.Contains(search, StringComparison.OrdinalIgnoreCase);
            bool matchCat = cat == "все" || text.Contains(cat, StringComparison.OrdinalIgnoreCase);
            _merchantStockList.SetItemCheckState(i, _merchantStockList.GetItemCheckState(i)); // force visual update
        }

        // We can't easily filter CheckedListBox, but we'll use a workaround: redraw with visibility
        // Actually CheckedListBox doesn't support item visibility filtering well.
        // Let's just scroll to matching items. Better approach: reload filtered list.
        ReloadMerchantList(search, cat);
    }

    private void ReloadMerchantList(string search, string cat)
    {
        var checkedIds = new HashSet<string>();
        foreach (var item in _merchantStockList.CheckedItems)
        {
            var text = item?.ToString() ?? "";
            int sep = text.IndexOf("  —  ");
            if (sep > 0) checkedIds.Add(text.Substring(0, sep).Trim());
        }

        _merchantStockList.Items.Clear();

        var items = new List<(string Id, string Name, string Type)>();
        using (var conn = new SqliteConnection($"Data Source={_dbFile}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, name, type FROM items WHERE type <> 'collectible' ORDER BY id";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                items.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }

        foreach (var (id, name, type) in items)
        {
            string display = $"{id}  —  {name}  [{type}]";
            bool matchSearch = string.IsNullOrWhiteSpace(search) || display.Contains(search, StringComparison.OrdinalIgnoreCase);
            bool matchCat = cat == "все" || type.Contains(cat, StringComparison.OrdinalIgnoreCase);
            if (matchSearch && matchCat)
                _merchantStockList.Items.Add(display, checkedIds.Contains(id));
        }
    }

    // === LOAD ALL ===

    private void LoadAll()
    {
        var connStr = $"Data Source={_dbFile}";
        DbMigrationRunner.RunMigrations(connStr);

        LoadMonsterRefs();
        LoadCollectibleRefs();
        LoadNpcRefs();
        LoadQuestRefs();
        LoadRewardItemRefs();
        LoadItems();
        LoadMonsters();
        LoadLoot();
        LoadQuests();
        LoadWorld();
        LoadMerchantStockEditor();
        LoadAccounts();
    }

    private void LoadMonsterRefs() => _monsterRefs = LoadRefs("SELECT id, name FROM monsters ORDER BY id");
    private void LoadCollectibleRefs() => _collectibleRefs = LoadRefs("SELECT id, name FROM items WHERE type='collectible' ORDER BY id");
    private void LoadNpcRefs() => _npcRefs = LoadRefs("SELECT id, name FROM npcs ORDER BY id");
    private void LoadQuestRefs() => _questRefs = LoadRefs("SELECT id, title FROM quests_def ORDER BY id");
    private void LoadRewardItemRefs() => _rewardItemRefs = LoadRefs("SELECT id, name FROM items WHERE type <> 'collectible' ORDER BY id");

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

    // === ITEMS ===

    private void LoadItems()
    {
        _itemsGrid.DataSource = LoadTable(@"SELECT id, name, type, value, damage_min, damage_max, defense, max_health_bonus, heal_amount, stock, description,
            bonus_strength, bonus_endurance, bonus_agility, bonus_cunning, bonus_intellect, bonus_wisdom, bonus_crit_chance, bonus_crit_damage, bonus_evade_chance, two_handed,
            damage_type, attack_speed_modifier, weapon_subtype, attack_range
            FROM items ORDER BY id");
        SetupItemsTypeColumn();
        ApplyItemTypeView();
    }

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
                DataPropertyName = "type"};
            combo.Items.AddRange(new object[] { "weapon", "twohand", "shield", "helmet", "cloak", "chest", "legs", "boots", "glove", "belt", "necklace", "ring", "accessory", "consumable", "collectible", "trophy" });
            _itemsGrid.Columns.Insert(idx, combo);
        }
    }

    private void ApplyItemTypeView()
    {
        string selected = _itemTypeSelector?.SelectedItem?.ToString() ?? "все";

        if (_itemsGrid.DataSource is DataTable dt)
        {
            string search = GetSearchText(_itemsSearch);
            string filter = "";
            if (selected != "все") filter = $"type = '{selected}'";
            if (!string.IsNullOrWhiteSpace(search))
            {
                string sFilter = $"(name LIKE '%{search}%' OR id LIKE '%{search}%')";
                filter = string.IsNullOrEmpty(filter) ? sFilter : $"({filter}) AND {sFilter}";
            }
            dt.DefaultView.RowFilter = filter;
        }

        var alwaysVisible = new HashSet<string> { "id", "name", "type", "value", "stock", "description", "two_handed" };
        var relevant = selected switch
        {
            "weapon" or "twohand" => new HashSet<string> { "damage_min", "damage_max", "bonus_strength", "bonus_agility", "bonus_crit_chance", "bonus_crit_damage", "bonus_evade_chance", "bonus_parry_chance", "damage_type", "attack_speed_modifier", "weapon_subtype", "attack_range" },
            "shield" or "helmet" or "cloak" or "chest" or "legs" or "boots" or "glove" or "belt" => new HashSet<string> { "defense", "max_health_bonus", "bonus_endurance", "bonus_wisdom", "bonus_evade_chance", "bonus_block_chance", "bonus_parry_chance" },
            "accessory" or "necklace" or "ring" => new HashSet<string> { "damage_min", "damage_max", "defense", "max_health_bonus",
                "bonus_strength", "bonus_endurance", "bonus_agility", "bonus_cunning", "bonus_intellect", "bonus_wisdom",
                "bonus_crit_chance", "bonus_crit_damage", "bonus_evade_chance", "bonus_block_chance", "bonus_parry_chance" },
            "consumable" => new HashSet<string> { "heal_amount", "max_health_bonus" },
            "collectible" => new HashSet<string> { },
            "trophy" => new HashSet<string> { "damage_min", "damage_max", "defense", "max_health_bonus", "heal_amount",
                "bonus_strength", "bonus_endurance", "bonus_agility", "bonus_cunning", "bonus_intellect", "bonus_wisdom",
                "bonus_crit_chance", "bonus_crit_damage", "bonus_evade_chance", "bonus_block_chance", "bonus_parry_chance" },
            _ => null
        };

        foreach (DataGridViewColumn col in _itemsGrid.Columns)
        {
            if (col.Name == "type") { col.Visible = true; continue; }
            col.Visible = relevant == null || alwaysVisible.Contains(col.Name) || relevant.Contains(col.Name);
        }
    }

    // === MONSTERS ===

    private void LoadMonsters()
    {
        _monstersGrid.DataSource = LoadTable(@"SELECT id, name, tier, health, phys_attack, phys_defense, xp_reward, gold_reward, symbol,
            strength, endurance, agility, cunning, intellect, wisdom, crit_chance, crit_damage, evade_chance, block_chance, parry_chance, shield_defense
            FROM monsters ORDER BY id");
    }

    // === LOOT ===

    private void LoadLoot()
    {
        var dt = new DataTable();
        dt.Columns.Add("monster_name", typeof(string));
        dt.Columns.Add("name", typeof(string));
        dt.Columns.Add("description", typeof(string));
        dt.Columns.Add("value", typeof(int));
        dt.Columns.Add("drop_chance", typeof(int));

        using var conn = new SqliteConnection($"Data Source={_dbFile}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT l.monster_id, l.name, l.description, l.value, l.drop_chance, m.name
            FROM loot_tables l
            LEFT JOIN monsters m ON l.monster_id = m.id
            ORDER BY l.monster_id, l.id";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            string monsterName = reader.IsDBNull(5) ? reader.GetString(0) : reader.GetString(5);
            dt.Rows.Add(monsterName, reader.GetString(1), reader.GetString(2), reader.GetInt32(3), reader.GetInt32(4));
        }
        _lootGrid.DataSource = dt;

        // Rename column header
        if (_lootGrid.Columns.Contains("monster_name"))
            _lootGrid.Columns["monster_name"].HeaderText = "Монстр";
    }

    // === QUESTS ===

    private void LoadQuests()
    {
        BuildQuestsGrid();
        var dt = new DataTable();
        dt.Columns.Add("id", typeof(string));
        dt.Columns.Add("title", typeof(string));
        dt.Columns.Add("description", typeof(string));
        dt.Columns.Add("type", typeof(string));
        dt.Columns.Add("monster", typeof(string));
        dt.Columns.Add("item", typeof(string));
        dt.Columns.Add("npc", typeof(string));
        dt.Columns.Add("target", typeof(string));
        dt.Columns.Add("xp_reward", typeof(string));
        dt.Columns.Add("gold_reward", typeof(string));
        dt.Columns.Add("chain_id", typeof(string));
        dt.Columns.Add("step", typeof(string));
        dt.Columns.Add("prereq", typeof(string));
        dt.Columns.Add("min_level", typeof(string));
        dt.Columns.Add("item_reward", typeof(string));
        dt.Columns.Add("item_reward_count", typeof(string));

        using var conn = new SqliteConnection($"Data Source={_dbFile}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT id, title, description, type, target_monster_id, target_item_id, target_npc_id, target,
                xp_reward, gold_reward, chain_id, step, prerequisite_quest_id, min_level, item_reward_id, item_reward_count
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
            dt.Rows.Add(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                NameById(_monsterRefs, mid), NameById(_collectibleRefs, iid), NameById(_npcRefs, nid),
                reader.GetInt32(7).ToString(), reader.GetInt32(8).ToString(), reader.GetInt32(9).ToString(),
                ch, reader.GetInt32(11).ToString(), pr, reader.GetInt32(13).ToString(),
                NameById(_rewardItemRefs, rid), reader.GetInt32(15).ToString());
        }
        _questsGrid.DataSource = dt;
    }

    private void BuildQuestsGrid()
    {
        _questsGrid.Columns.Clear();
        _questsGrid.AutoGenerateColumns = false;
        void AddText(string name, string header)
        {
            _questsGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = name, HeaderText = header, Name = name });
        }
        AddText("id", "ID");
        AddText("title", "Название");
        AddText("description", "Описание");
        _questsGrid.Columns.Add(new DataGridViewComboBoxColumn
        {
            DataPropertyName = "type", HeaderText = "Тип", Name = "type",
            Items = { "kill", "collect", "talk", "travel", "use" }
        });
        _questsGrid.Columns.Add(new DataGridViewComboBoxColumn
        {
            DataPropertyName = "monster", HeaderText = "Монстр (kill)", Name = "monster",
            DataSource = _monsterRefs.Select(r => r.Name).ToList()});
        _questsGrid.Columns.Add(new DataGridViewComboBoxColumn
        {
            DataPropertyName = "item", HeaderText = "Предмет (collect)", Name = "item",
            DataSource = _collectibleRefs.Select(r => r.Name).ToList()});
        _questsGrid.Columns.Add(new DataGridViewComboBoxColumn
        {
            DataPropertyName = "npc", HeaderText = "NPC (talk)", Name = "npc",
            DataSource = _npcRefs.Select(r => r.Name).ToList()});
        AddText("target", "Кол-во");
        AddText("xp_reward", "Опыт");
        AddText("gold_reward", "Золото");
        AddText("chain_id", "Цепочка");
        AddText("step", "Шаг");
        AddText("prereq", "Предусловие (ID)");
        AddText("min_level", "Мин. ур.");
        _questsGrid.Columns.Add(new DataGridViewComboBoxColumn
        {
            DataPropertyName = "item_reward", HeaderText = "Награда (предмет)", Name = "item_reward",
            DataSource = _rewardItemRefs.Select(r => r.Name).ToList()});
        AddText("item_reward_count", "Награда (кол-во)");
    }

    // === WORLD ===

    private void LoadWorld()
    {
        BuildWorldGrid();
        var dt = new DataTable();
        dt.Columns.Add("id", typeof(string));
        dt.Columns.Add("name", typeof(string));
        dt.Columns.Add("type", typeof(string));

        using var conn = new SqliteConnection($"Data Source={_dbFile}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, type FROM npcs ORDER BY id";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            dt.Rows.Add(reader.GetString(0), reader.GetString(1), reader.GetString(2));
        _worldGrid.DataSource = dt;
        _worldWidth = GetWorldConfigInt("width", 100);
        _worldHeight = GetWorldConfigInt("height", 100);
    }

    private void BuildWorldGrid()
    {
        _worldGrid.Columns.Clear();
        _worldGrid.DataSource = null;
        _worldGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "id", HeaderText = "ID", DataPropertyName = "id", ReadOnly = true });
        _worldGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "name", HeaderText = "Имя", DataPropertyName = "name" });
        _worldGrid.Columns.Add(new DataGridViewComboBoxColumn
        {
            Name = "type", HeaderText = "Тип", DataPropertyName = "type",
            Items = { "merchant", "board", "npc", "instance_portal" }
        });
    }

    // === MERCHANT ===

    private void LoadMerchantStockEditor()
    {
        try
        {
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
                while (reader.Read()) stock.Add(reader.GetString(0));
            }

            _merchantStockList.Items.Clear();
            var items = new List<(string Id, string Name, string Type)>();
            using (var conn = new SqliteConnection($"Data Source={_dbFile}"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT id, name, type FROM items WHERE type <> 'collectible' ORDER BY id";
                using var reader = cmd.ExecuteReader();
                while (reader.Read()) items.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
            }

            foreach (var (id, name, type) in items)
                _merchantStockList.Items.Add($"{id}  —  {name}  [{type}]", stock.Contains(id));

            SetStatus("Ассортимент торговца загружен");
        }
        catch (Exception ex) { SetStatus("Ошибка (ассортимент): " + ex.Message); }
    }

    // === ANIMATIONS ===

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

        _animGrid = MakeGrid();
        _animGrid.AllowUserToAddRows = false;
        _animGrid.AllowUserToDeleteRows = false;
        _animGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "key", HeaderText = "Ключ", DataPropertyName = "key" });
        _animGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "sheet", HeaderText = "Файл", DataPropertyName = "sheet", ReadOnly = true });
        _animGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "cols", HeaderText = "Колонки", DataPropertyName = "cols" });
        _animGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "rows", HeaderText = "Строки", DataPropertyName = "rows" });
        _animGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "fps", HeaderText = "FPS", DataPropertyName = "fps" });
        _animGrid.SelectionChanged += (s, e) => UpdateAnimPreview();
        _animGrid.CellValueChanged += (s, e) => UpdateAnimPreview();

        _animPreview = new PictureBox
        {
            Dock = DockStyle.Right,
            Width = 170,
            BackColor = System.Drawing.Color.FromArgb(20, 22, 28),
            SizeMode = PictureBoxSizeMode.CenterImage};

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
        catch (Exception ex) { SetStatus("Ошибка чтения animations.json: " + ex.Message); }
    }

    private void AddAnimation()
    {
        using var dlg = new OpenFileDialog { Filter = "PNG спрайт-лист|*.png", Title = "Выберите PNG спрайт-лист" };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        string path = dlg.FileName;
        string fileName = Path.GetFileName(path);
        string key = Path.GetFileNameWithoutExtension(path);
        int cols = 4, rows = 1, fps = 8;
        try { using var img = System.Drawing.Image.FromFile(path); cols = Math.Max(1, (int)Math.Round((double)img.Width / Math.Max(1, img.Height))); } catch { }

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
        SetStatus($"Анимация '{key}' добавлена");
    }

    private void DeleteAnimation()
    {
        if (_animGrid.SelectedRows.Count == 0) return;
        var row = _animGrid.SelectedRows[0];
        string? sheet = row.Cells["sheet"].Value?.ToString();
        _animGrid.Rows.Remove(row);
        if (sheet != null) _animSrcPaths.Remove(sheet);
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
        try { _animPreviewImage = System.Drawing.Image.FromFile(path); } catch { _animPreviewImage = null; }
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
        int targetH = Math.Max(1, (int)((double)targetW / fw * fh));
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
                entries.Add(new AnimEntry { Key = key, Sheet = sheet, Cols = Math.Max(1, ToInt(row.Cells["cols"].Value)), Rows = Math.Max(1, ToInt(row.Cells["rows"].Value)), Fps = Math.Max(1, ToInt(row.Cells["fps"].Value)) });
            }
            string json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
            foreach (var content in new[] { ClientBinContent(), ClientSrcContent() })
            {
                Directory.CreateDirectory(content);
                Directory.CreateDirectory(Path.Combine(content, "Animations"));
                File.WriteAllText(Path.Combine(content, "animations.json"), json);
                foreach (var e in entries)
                    if (_animSrcPaths.TryGetValue(e.Sheet, out var src) && File.Exists(src))
                        File.Copy(src, Path.Combine(content, "Animations", e.Sheet), true);
            }
            SetStatus($"Анимации сохранены: {entries.Count}");
        }
        catch (Exception ex) { SetStatus("Ошибка (анимации): " + ex.Message); }
    }

    // === SAVE METHODS ===

    private void SaveCurrentTab()
    {
        var result = MessageBox.Show("Сохранить изменения?", "Подтверждение", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (result != DialogResult.Yes) return;

        var idx = _tabs.SelectedIndex;
        if (idx == _tabs.TabPages.IndexOf(_itemsTab)) SaveItems();
        else if (idx == _tabs.TabPages.IndexOf(_monstersTab)) SaveMonsters();
        else if (idx == _tabs.TabPages.IndexOf(_lootTab)) SaveLoot();
        else if (idx == _tabs.TabPages.IndexOf(_questsTab)) SaveQuests();
        else if (idx == _tabs.TabPages.IndexOf(_worldTab)) SaveWorld();
        else if (idx == _tabs.TabPages.IndexOf(_merchantTab)) SaveMerchantStockEditor();
        else if (idx == _tabs.TabPages.IndexOf(_accountsTab)) SaveAccounts();
        else if (idx == _tabs.TabPages.IndexOf(_animTab)) SaveAnimations();
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
            using (var del = conn.CreateCommand()) { del.CommandText = "DELETE FROM items"; del.ExecuteNonQuery(); }
            foreach (DataRow row in dt.Rows)
            {
                if (row.RowState == DataRowState.Deleted) continue;
                if (string.IsNullOrWhiteSpace(row["id"]?.ToString())) continue;
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"INSERT INTO items (id, name, type, value, damage_min, damage_max, defense, max_health_bonus, heal_amount, stock, description,
                        bonus_strength, bonus_endurance, bonus_agility, bonus_cunning, bonus_intellect, bonus_wisdom, bonus_crit_chance, bonus_crit_damage, bonus_evade_chance, two_handed,
                        damage_type, attack_speed_modifier, weapon_subtype, attack_range)
                    VALUES ($id,$n,$t,$v,$dmn,$dmx,$d,$m,$h,$s,$desc,$str,$sta,$agi,$cun,$wis,$wil,$cc,$cd,$ec,$th,$dt,$asm,$ws,$ar)";
                cmd.Parameters.AddWithValue("$id", row["id"]);
                cmd.Parameters.AddWithValue("$n", row["name"] ?? "");
                cmd.Parameters.AddWithValue("$t", row["type"] ?? "");
                cmd.Parameters.AddWithValue("$v", ToInt(row["value"]));
                cmd.Parameters.AddWithValue("$dmn", ToInt(row["damage_min"]));
                cmd.Parameters.AddWithValue("$dmx", ToInt(row["damage_max"]));
                cmd.Parameters.AddWithValue("$d", ToInt(row["defense"]));
                cmd.Parameters.AddWithValue("$m", ToInt(row["max_health_bonus"]));
                cmd.Parameters.AddWithValue("$h", ToInt(row["heal_amount"]));
                cmd.Parameters.AddWithValue("$s", ToInt(row["stock"]));
                cmd.Parameters.AddWithValue("$desc", row["description"] ?? "");
                cmd.Parameters.AddWithValue("$str", ToInt(row["bonus_strength"]));
                cmd.Parameters.AddWithValue("$sta", ToInt(row["bonus_endurance"]));
                cmd.Parameters.AddWithValue("$agi", ToInt(row["bonus_agility"]));
                cmd.Parameters.AddWithValue("$cun", ToInt(row["bonus_cunning"]));
                cmd.Parameters.AddWithValue("$wis", ToInt(row["bonus_intellect"]));
                cmd.Parameters.AddWithValue("$wil", ToInt(row["bonus_wisdom"]));
                cmd.Parameters.AddWithValue("$cc", ToDouble(row["bonus_crit_chance"]));
                cmd.Parameters.AddWithValue("$cd", ToDouble(row["bonus_crit_damage"]));
                cmd.Parameters.AddWithValue("$ec", ToDouble(row["bonus_evade_chance"]));
                cmd.Parameters.AddWithValue("$th", ToInt(row["two_handed"]));
                cmd.Parameters.AddWithValue("$dt", row["damage_type"] ?? "");
                cmd.Parameters.AddWithValue("$asm", ToDouble(row["attack_speed_modifier"]));
                cmd.Parameters.AddWithValue("$ws", row["weapon_subtype"] ?? "");
                cmd.Parameters.AddWithValue("$ar", ToInt(row["attack_range"]));
                cmd.ExecuteNonQuery();
            }
            transaction.Commit();
            LoadItems();
            SetStatus("Предметы сохранены");
        }
        catch (Exception ex) { SetStatus("Ошибка (предметы): " + ex.Message); }
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
                cmd.CommandText = @"INSERT INTO monsters (id, name, tier, health, phys_attack, phys_defense, xp_reward, gold_reward, symbol,
                        strength, endurance, agility, cunning, intellect, wisdom, crit_chance, crit_damage, evade_chance)
                    VALUES ($id,$n,$t,$hp,$a,$d,$xp,$g,$s,$str,$sta,$agi,$cun,$wis,$wil,$cc,$cd,$ec)";
                cmd.Parameters.AddWithValue("$id", row["id"]);
                cmd.Parameters.AddWithValue("$n", row["name"] ?? "");
                cmd.Parameters.AddWithValue("$t", ToInt(row["tier"]));
                cmd.Parameters.AddWithValue("$hp", ToInt(row["health"]));
                cmd.Parameters.AddWithValue("$a", ToInt(row["phys_attack"]));
                cmd.Parameters.AddWithValue("$d", ToInt(row["phys_defense"]));
                cmd.Parameters.AddWithValue("$xp", ToInt(row["xp_reward"]));
                cmd.Parameters.AddWithValue("$g", ToInt(row["gold_reward"]));
                cmd.Parameters.AddWithValue("$s", (row["symbol"]?.ToString() ?? "M").Length > 0 ? row["symbol"].ToString()![0].ToString() : "M");
                cmd.Parameters.AddWithValue("$str", ToInt(row["strength"]));
                cmd.Parameters.AddWithValue("$sta", ToInt(row["endurance"]));
                cmd.Parameters.AddWithValue("$agi", ToInt(row["agility"]));
                cmd.Parameters.AddWithValue("$cun", ToInt(row["cunning"]));
                cmd.Parameters.AddWithValue("$wis", ToInt(row["intellect"]));
                cmd.Parameters.AddWithValue("$wil", ToInt(row["wisdom"]));
                cmd.Parameters.AddWithValue("$cc", ToDouble(row["crit_chance"]));
                cmd.Parameters.AddWithValue("$cd", ToDouble(row["crit_damage"]));
                cmd.Parameters.AddWithValue("$ec", ToDouble(row["evade_chance"]));
                cmd.ExecuteNonQuery();
            }
            transaction.Commit();
            LoadMonsters();
            SetStatus("Монстры сохранены");
        }
        catch (Exception ex) { SetStatus("Ошибка (монстры): " + ex.Message); }
    }

    private void SaveLoot()
    {
        try
        {
            _lootGrid.EndEdit();
            var dt = (DataTable)_lootGrid.DataSource!;
            var monsterNameToId = _monsterRefs.ToDictionary(r => r.Name, r => r.Id);
            using var conn = new SqliteConnection($"Data Source={_dbFile}");
            conn.Open();
            using var transaction = conn.BeginTransaction();
            using (var del = conn.CreateCommand()) { del.CommandText = "DELETE FROM loot_tables"; del.ExecuteNonQuery(); }
            foreach (DataRow row in dt.Rows)
            {
                if (row.RowState == DataRowState.Deleted) continue;
                string monsterName = row["monster_name"]?.ToString() ?? "";
                string name = row["name"]?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(monsterName) || string.IsNullOrWhiteSpace(name)) continue;
                string monsterId = monsterNameToId.TryGetValue(monsterName, out var mid) ? mid : monsterName;
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
        catch (Exception ex) { SetStatus("Ошибка (лут): " + ex.Message); }
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
                string monsterId = type == "kill" ? IdByName(_monsterRefs, row["monster"]?.ToString() ?? "") : "";
                string itemId = type == "collect" ? IdByName(_collectibleRefs, row["item"]?.ToString() ?? "") : "";
                string npcId = type == "talk" ? IdByName(_npcRefs, row["npc"]?.ToString() ?? "") : "";
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"INSERT INTO quests_def (id, title, description, type, target_monster_id, target_item_id, target_npc_id, target, xp_reward, gold_reward, chain_id, step, prerequisite_quest_id, min_level, item_reward_id, item_reward_count)
                    VALUES ($id,$t,$d,$ty,$tm,$ti,$tn,$tg,$xp,$g,$ch,$st,$pr,$ml,$ri,$rc)";
                cmd.Parameters.AddWithValue("$id", row["id"]);
                cmd.Parameters.AddWithValue("$t", row["title"] ?? "");
                cmd.Parameters.AddWithValue("$d", row["description"] ?? "");
                cmd.Parameters.AddWithValue("$ty", type);
                cmd.Parameters.AddWithValue("$tm", monsterId);
                cmd.Parameters.AddWithValue("$ti", itemId);
                cmd.Parameters.AddWithValue("$tn", npcId);
                cmd.Parameters.AddWithValue("$tg", ToInt(row["target"]));
                cmd.Parameters.AddWithValue("$xp", ToInt(row["xp_reward"]));
                cmd.Parameters.AddWithValue("$g", ToInt(row["gold_reward"]));
                cmd.Parameters.AddWithValue("$ch", row["chain_id"] ?? "");
                cmd.Parameters.AddWithValue("$st", ToInt(row["step"]));
                cmd.Parameters.AddWithValue("$pr", row["prereq"] ?? "");
                cmd.Parameters.AddWithValue("$ml", ToInt(row["min_level"]));
                cmd.Parameters.AddWithValue("$ri", IdByName(_rewardItemRefs, row["item_reward"]?.ToString() ?? ""));
                cmd.Parameters.AddWithValue("$rc", ToInt(row["item_reward_count"]));
                cmd.ExecuteNonQuery();
            }
            transaction.Commit();
            LoadQuests();
            SetStatus("Квесты сохранены");
        }
        catch (Exception ex) { SetStatus("Ошибка (квесты): " + ex.Message); }
    }

    private void SaveWorld()
    {
        try
        {
            _worldGrid.EndEdit();
            var npcs = new List<NpcRecord>();
            int maxNum = 0;
            foreach (DataGridViewRow row in _worldGrid.Rows)
            {
                if (row.IsNewRow) continue;
                string id = CellStr(row, "id");
                string name = CellStr(row, "name");
                string type = CellStr(row, "type");
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(type)) continue;
                if (string.IsNullOrWhiteSpace(id)) id = "N" + (maxNum + 1).ToString("D4");
                if (id.StartsWith("N") && int.TryParse(id.Substring(1), out int n) && n > maxNum) maxNum = n;
                npcs.Add(new NpcRecord { Id = id, Name = name, Type = type });
            }
            SaveNpcsLocal(npcs);
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
        catch (Exception ex) { SetStatus("Ошибка (мир): " + ex.Message); }
    }

    private void SaveMerchantStockEditor()
    {
        try
        {
            var merchantId = GetMerchantNpcId();
            if (merchantId == null) { SetStatus("Нет NPC типа 'merchant'"); return; }
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
            using (var del = conn.CreateCommand()) { del.CommandText = "DELETE FROM merchant_stock WHERE npc_id = $npc"; del.Parameters.AddWithValue("$npc", merchantId); del.ExecuteNonQuery(); }
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
        catch (Exception ex) { SetStatus("Ошибка (ассортимент): " + ex.Message); }
    }

    // === ACCOUNTS TAB ===

    private DataGridView _accountsGrid = null!;
    private DataGridView _inventoryGrid = null!;
    private TextBox _accountsSearch = null!;
    private Button _banBtn = null!;
    private Button _adminBtn = null!;
    private Button _resetPwdBtn = null!;
    private Button _giveItemBtn = null!;
    private Label _selectedPlayerLabel = null!;

    private TabPage BuildAccountsTab()
    {
        var tab = new TabPage("Аккаунты");

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal};

        // ── Top panel: accounts grid + buttons ──
        var topPanel = new Panel { Dock = DockStyle.Fill };

        var accountsSearchRow = new Panel { Dock = DockStyle.Top, Height = 28, Padding = new Padding(6, 2, 6, 2) };
        _accountsSearch = MakeSearchBox("Поиск аккаунтов...");
        _accountsSearch.TextChanged += (s, e) => ApplyAccountsFilter();
        accountsSearchRow.Controls.Add(_accountsSearch);

        var actionRow = new Panel { Dock = DockStyle.Top, Height = 34, Padding = new Padding(6, 3, 6, 3) };

        _banBtn = MakeSmallButton("Забанить", SystemColors.ControlDarkDark);
        _banBtn.Click += (s, e) => ToggleBan();
        _adminBtn = MakeSmallButton("Админ", SystemColors.ControlDark);
        _adminBtn.Click += (s, e) => ToggleAdmin();
        _resetPwdBtn = MakeSmallButton("Сбросить пароль", SystemColors.ControlLight);
        _resetPwdBtn.Click += (s, e) => ResetPassword();
        _giveItemBtn = MakeSmallButton("Выдать предмет", SystemColors.ControlDark);
        _giveItemBtn.Click += (s, e) => GiveItem();

        var saveAccountsBtn = MakeSmallButton("Сохранить", SystemColors.ControlDark);
        saveAccountsBtn.Click += (s, e) => SaveAccounts();

        actionRow.Controls.Add(saveAccountsBtn);
        actionRow.Controls.Add(_giveItemBtn);
        actionRow.Controls.Add(_resetPwdBtn);
        actionRow.Controls.Add(_adminBtn);
        actionRow.Controls.Add(_banBtn);

        _accountsGrid = MakeGrid();
        _accountsGrid.AllowUserToAddRows = true;
        _accountsGrid.AllowUserToDeleteRows = true;
        _accountsGrid.SelectionChanged += (s, e) => LoadPlayerInventory();

        topPanel.Controls.Add(_accountsGrid);
        topPanel.Controls.Add(actionRow);
        topPanel.Controls.Add(accountsSearchRow);

        split.Panel1.Controls.Add(topPanel);

        // ── Bottom panel: inventory/equipment of selected account ──
        var bottomPanel = new Panel { Dock = DockStyle.Fill };

        _selectedPlayerLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 24,
            Text = "Выберите аккаунт",
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 0, 0)};

        _inventoryGrid = MakeGrid();
        _inventoryGrid.AllowUserToAddRows = false;
        _inventoryGrid.AllowUserToDeleteRows = false;

        bottomPanel.Controls.Add(_inventoryGrid);
        bottomPanel.Controls.Add(_selectedPlayerLabel);
        split.Panel2.Controls.Add(bottomPanel);

        split.SplitterDistance = (int)(split.Height * 0.65);
        tab.Controls.Add(split);
        return tab;
    }

    private void ApplyAccountsFilter()
    {
        if (_accountsGrid.DataSource is not DataTable dt) return;
        string search = GetSearchText(_accountsSearch);
        if (string.IsNullOrWhiteSpace(search)) { dt.DefaultView.RowFilter = ""; return; }
        dt.DefaultView.RowFilter = $"login LIKE '%{search}%' OR player_name LIKE '%{search}%'";
    }

    private void LoadAccounts()
    {
        var dt = new DataTable();
        using (var conn = new SqliteConnection($"Data Source={_dbFile}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM accounts ORDER BY login";
            using var reader = cmd.ExecuteReader();
            dt.Load(reader);
        }

        _accountsGrid.DataSource = dt;

        foreach (DataGridViewColumn col in _accountsGrid.Columns)
        {
            if (col.Name is "login" or "player_name" or "ban_reason" or "current_zone")
                col.AutoSizeMode = DataGridViewAutoSizeColumnMode.DisplayedCells;
            else if (col.Name is "is_admin" or "is_banned")
            {
                col.ValueType = typeof(bool);
                col.DefaultCellStyle.BackColor = Color.FromArgb(55, 40, 40);
            }
        }

        // Hide technical columns
        var hiddenCols = new[] { "password_hash", "created_at", "last_login",
            "weapon_id", "armor_id", "accessory_id",
            "hotbar_slots", "learned_skills" };
        foreach (var name in hiddenCols)
            if (_accountsGrid.Columns[name] != null)
                _accountsGrid.Columns[name].Visible = false;

        // Move is_admin, is_banned to the end
        if (_accountsGrid.Columns["is_admin"] != null)
            _accountsGrid.Columns["is_admin"].DisplayIndex = _accountsGrid.ColumnCount - 1;
        if (_accountsGrid.Columns["is_banned"] != null)
            _accountsGrid.Columns["is_banned"].DisplayIndex = _accountsGrid.ColumnCount - 1;
    }

    private void LoadPlayerInventory()
    {
        if (_accountsGrid.SelectedRows.Count == 0 || _accountsGrid.SelectedRows[0].IsNewRow)
        {
            _inventoryGrid.DataSource = null;
            _selectedPlayerLabel.Text = "Выберите аккаунт";
            return;
        }

        var row = _accountsGrid.SelectedRows[0];
        string login = row.Cells["login"]?.Value?.ToString() ?? "";
        string playerName = row.Cells["player_name"]?.Value?.ToString() ?? "";
        _selectedPlayerLabel.Text = $"Инвентарь: {login} ({playerName})";

        var dt = new DataTable();
        using (var conn = new SqliteConnection($"Data Source={_dbFile}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT i.id, i.item_id, i.name, i.type, i.quantity, i.value,
                       i.damage_min, i.damage_max, i.defense
                FROM inventory i
                WHERE i.player_name = $p
                ORDER BY i.id";
            cmd.Parameters.AddWithValue("$p", playerName);
            using var reader = cmd.ExecuteReader();
            dt.Load(reader);
        }

        _inventoryGrid.DataSource = dt;
    }

    private void ToggleBan()
    {
        if (_accountsGrid.SelectedRows.Count == 0) return;
        var row = _accountsGrid.SelectedRows[0];
        if (row.IsNewRow) return;

        bool current = IsChecked(row.Cells["is_banned"]);
        row.Cells["is_banned"].Value = current ? 0 : 1;
        if (!current)
            row.Cells["ban_reason"].Value = "Нарушение правил";
        else
            row.Cells["ban_reason"].Value = "";
        SetStatus(current ? "Аккаунт разбанен" : "Аккаунт забанен");
    }

    private void ToggleAdmin()
    {
        if (_accountsGrid.SelectedRows.Count == 0) return;
        var row = _accountsGrid.SelectedRows[0];
        if (row.IsNewRow) return;

        bool current = IsChecked(row.Cells["is_admin"]);
        row.Cells["is_admin"].Value = current ? 0 : 1;
        SetStatus(current ? "Админ права сняты" : "Админ права выданы");
    }

    private void ResetPassword()
    {
        if (_accountsGrid.SelectedRows.Count == 0) return;
        var row = _accountsGrid.SelectedRows[0];
        if (row.IsNewRow) return;

        string login = row.Cells["login"]?.Value?.ToString() ?? "";
        var result = MessageBox.Show($"Сбросить пароль для {login} на '123'?", "Сброс пароля",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (result != DialogResult.Yes) return;

        using var conn = new SqliteConnection($"Data Source={_dbFile}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE accounts SET password_hash = $p WHERE login = $l";
        cmd.Parameters.AddWithValue("$p", HashPassword("123"));
        cmd.Parameters.AddWithValue("$l", login);
        cmd.ExecuteNonQuery();
        SetStatus($"Пароль для {login} сброшен на '123'");
    }

    private static string HashPassword(string password)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        byte[] bytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(password));
        return Convert.ToBase64String(bytes);
    }

    private void GiveItem()
    {
        if (_accountsGrid.SelectedRows.Count == 0) return;
        var row = _accountsGrid.SelectedRows[0];
        if (row.IsNewRow) return;

        string login = row.Cells["login"]?.Value?.ToString() ?? "";
        string playerName = row.Cells["player_name"]?.Value?.ToString() ?? "";

        // Get all item templates
        var items = new List<(string Id, string Name, string Type)>();
        using (var conn = new SqliteConnection($"Data Source={_dbFile}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, name, type FROM items ORDER BY id";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                items.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }

        if (items.Count == 0) { MessageBox.Show("Нет предметов в базе", "Ошибка"); return; }

        // Show selection dialog
        var pickForm = new Form
        {
            Text = "Выдать предмет",
            Size = new Size(500, 500),
            StartPosition = FormStartPosition.CenterParent,
        };

        var searchBox = new TextBox
        {
            Dock = DockStyle.Top,
            BorderStyle = BorderStyle.FixedSingle
        };
        searchBox.GotFocus += (s, e) => { if (searchBox.Text == "Поиск...") { searchBox.Text = ""; searchBox.ForeColor = SystemColors.WindowText; } };
        searchBox.LostFocus += (s, e) => { if (string.IsNullOrWhiteSpace(searchBox.Text)) { searchBox.Text = "Поиск..."; searchBox.ForeColor = SystemColors.GrayText; } };
        searchBox.Text = "Поиск...";
        searchBox.ForeColor = SystemColors.GrayText;

        var listBox = new ListBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            SelectionMode = SelectionMode.One
        };
        listBox.Items.AddRange(items.Select(i => $"{i.Id}  —  {i.Name}  [{i.Type}]").ToArray());

        searchBox.TextChanged += (s, e) =>
        {
            listBox.Items.Clear();
            string f = searchBox.ForeColor == SystemColors.GrayText ? "" : searchBox.Text.ToLower();
            listBox.Items.AddRange(items
                .Where(i => string.IsNullOrWhiteSpace(f) || i.Name.ToLower().Contains(f) || i.Id.ToLower().Contains(f))
                .Select(i => $"{i.Id}  —  {i.Name}  [{i.Type}]").ToArray());
        };

        var qtyPanel = new Panel { Dock = DockStyle.Bottom, Height = 40, Padding = new Padding(6) };
        var qtyLabel = new Label { Text = "Количество:", Dock = DockStyle.Left, Width = 80, TextAlign = ContentAlignment.MiddleLeft };
        var qtyBox = new NumericUpDown { Dock = DockStyle.Left, Width = 60, Minimum = 1, Maximum = 9999, Value = 1 };
        var okBtn = new Button
        {
            Text = "Выдать",
            Dock = DockStyle.Right,
            Width = 80,
            Height = 30,
            BackColor = SystemColors.ControlDark,
            ForeColor = SystemColors.WindowText,
            Cursor = Cursors.Hand
        };
        qtyPanel.Controls.Add(okBtn);
        qtyPanel.Controls.Add(qtyBox);
        qtyPanel.Controls.Add(qtyLabel);

        pickForm.Controls.Add(listBox);
        pickForm.Controls.Add(searchBox);
        pickForm.Controls.Add(qtyPanel);

        string? selectedItemLine = null;
        okBtn.Click += (s, e) =>
        {
            if (listBox.SelectedItem == null) { MessageBox.Show("Выберите предмет", "Ошибка"); return; }
            selectedItemLine = listBox.SelectedItem.ToString();
            pickForm.DialogResult = DialogResult.OK;
            pickForm.Close();
        };

        if (pickForm.ShowDialog() != DialogResult.OK || selectedItemLine == null) return;

        int sep = selectedItemLine.IndexOf("  —  ");
        string selectedId = selectedItemLine.Substring(0, sep).Trim();
        int qty = (int)qtyBox.Value;

        // Get the template
        var template = items.First(i => i.Id == selectedId);

        // Insert into inventory
        using (var conn = new SqliteConnection($"Data Source={_dbFile}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO inventory (player_name, item_id, name, type, value, quantity)
                VALUES ($p, $id, $n, $t, $v, $q)";
            cmd.Parameters.AddWithValue("$p", playerName);
            cmd.Parameters.AddWithValue("$id", template.Id);
            cmd.Parameters.AddWithValue("$n", template.Name);
            cmd.Parameters.AddWithValue("$t", template.Type);
            cmd.Parameters.AddWithValue("$v", 0);
            cmd.Parameters.AddWithValue("$q", qty);
            cmd.ExecuteNonQuery();
        }

        SetStatus($"Выдано {qty}x {template.Name} игроку {login}");
        LoadPlayerInventory();
    }

    private void SaveAccounts()
    {
        try
        {
            _accountsGrid.EndEdit();
            if (_accountsGrid.DataSource is not DataTable dt) return;

            EnsureId(dt, "A");
            using var conn = new SqliteConnection($"Data Source={_dbFile}");
            conn.Open();
            using var transaction = conn.BeginTransaction();

            using (var del = conn.CreateCommand()) { del.CommandText = "DELETE FROM accounts"; del.ExecuteNonQuery(); }

            foreach (DataRow dr in dt.Rows)
            {
                if (dr.RowState == DataRowState.Deleted) continue;
                if (string.IsNullOrWhiteSpace(dr["login"]?.ToString())) continue;

                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO accounts (login, password_hash, player_name, level, experience,
                        health, max_health, phys_attack, phys_defense, gold, created_at, last_login,
                        weapon_id, armor_id, accessory_id,
                        strength, endurance, agility, cunning, intellect, wisdom,
                        attribute_points, speed, pos_x, pos_y,
                        hotbar_slots, is_admin, is_banned, ban_reason, skill_points, learned_skills, current_zone)
                    VALUES ($l, $ph, $pn, $lv, $exp,
                        $hp, $mhp, $pa, $pd, $g, $ca, $ll,
                        $wi, $ai, $aci,
                        $str, $end, $agi, $cun, $int, $wis,
                        $ap, $spd, $px, $py,
                        $hs, $adm, $ban, $br, $skp, $ls, $cz)";

                cmd.Parameters.AddWithValue("$l", dr["login"]);
                cmd.Parameters.AddWithValue("$ph", dr.Table.Columns.Contains("password_hash")
                    && dr["password_hash"] != DBNull.Value
                    && !string.IsNullOrWhiteSpace(dr["password_hash"]?.ToString())
                    ? dr["password_hash"].ToString() : HashPassword("123"));
                cmd.Parameters.AddWithValue("$pn", dr["player_name"] ?? "");
                cmd.Parameters.AddWithValue("$lv", ToInt(dr["level"]));
                cmd.Parameters.AddWithValue("$exp", ToInt(dr["experience"]));
                cmd.Parameters.AddWithValue("$hp", ToInt(dr["health"]));
                cmd.Parameters.AddWithValue("$mhp", ToInt(dr["max_health"]));
                cmd.Parameters.AddWithValue("$pa", ToInt(dr["phys_attack"]));
                cmd.Parameters.AddWithValue("$pd", ToInt(dr["phys_defense"]));
                cmd.Parameters.AddWithValue("$g", ToInt(dr["gold"]));
                cmd.Parameters.AddWithValue("$ca", DateTime.UtcNow.ToString("O"));
                cmd.Parameters.AddWithValue("$ll", DateTime.UtcNow.ToString("O"));
                cmd.Parameters.AddWithValue("$wi", dr["weapon_id"] ?? "");
                cmd.Parameters.AddWithValue("$ai", dr["armor_id"] ?? "");
                cmd.Parameters.AddWithValue("$aci", dr["accessory_id"] ?? "");
                cmd.Parameters.AddWithValue("$str", ToInt(dr["strength"]));
                cmd.Parameters.AddWithValue("$end", ToInt(dr["endurance"]));
                cmd.Parameters.AddWithValue("$agi", ToInt(dr["agility"]));
                cmd.Parameters.AddWithValue("$cun", ToInt(dr["cunning"]));
                cmd.Parameters.AddWithValue("$int", ToInt(dr["intellect"]));
                cmd.Parameters.AddWithValue("$wis", ToInt(dr["wisdom"]));
                cmd.Parameters.AddWithValue("$ap", ToInt(dr["attribute_points"]));
                cmd.Parameters.AddWithValue("$spd", ToInt(dr["speed"]));
                cmd.Parameters.AddWithValue("$px", ToInt(dr["pos_x"]));
                cmd.Parameters.AddWithValue("$py", ToInt(dr["pos_y"]));
                cmd.Parameters.AddWithValue("$hs", dr["hotbar_slots"] ?? "");
                cmd.Parameters.AddWithValue("$adm", IsChecked(dr["is_admin"]) ? 1 : 0);
                cmd.Parameters.AddWithValue("$ban", IsChecked(dr["is_banned"]) ? 1 : 0);
                cmd.Parameters.AddWithValue("$br", dr["ban_reason"] ?? "");
                cmd.Parameters.AddWithValue("$skp", ToInt(dr["skill_points"]));
                cmd.Parameters.AddWithValue("$ls", dr["learned_skills"] ?? "[]");
                cmd.Parameters.AddWithValue("$cz", dr["current_zone"] ?? "main");
                cmd.ExecuteNonQuery();
            }

            transaction.Commit();
            SetStatus("Аккаунты сохранены");
        }
        catch (Exception ex) { SetStatus("Ошибка (аккаунты): " + ex.Message); }
    }

    private static bool IsChecked(object? v)
    {
        if (v is bool b) return b;
        if (v is int i) return i != 0;
        if (v is string s) return s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase);
        return false;
    }


    private void EditItemRow(DataGridViewRow row)
    {
        using var dlg = new ItemEditForm(row, _itemsGrid);
        if (dlg.ShowDialog() != DialogResult.OK) return;
        SetStatus("Предмет изменён");
    }

    internal class ItemEditForm : Form
    {
        private readonly DataGridView _grid;
        private readonly int _rowIndex;
        internal ItemEditForm(DataGridViewRow row, DataGridView grid) : base()
        {
            _grid = grid;
            _rowIndex = row.Index;
            Text = "Редактирование предмета";
            Size = new Size(420, 520);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Padding = new Padding(8);

            var top = new Panel { Dock = DockStyle.Top, AutoSize = true };
            top.Controls.Add(MakeField("ID:", _idBox = new TextBox { Dock = DockStyle.Fill }));
            _idBox.Text = Cell(row, "id");
            top.Controls.Add(MakeField("Name:", _nameBox = new TextBox { Dock = DockStyle.Fill }));
            _nameBox.Text = Cell(row, "name");
            top.Controls.Add(MakeField("Тип:", _typeCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList }));
            _typeCombo.Items.AddRange(new object[] { "weapon", "twohand", "shield", "helmet", "cloak", "chest", "legs", "boots", "glove", "belt", "necklace", "ring", "accessory", "consumable", "collectible", "trophy" });
            _typeCombo.SelectedItem = Cell(row, "type");

            top.Controls.Add(MakeField("Стоимость:", _valueBox = new NumericUpDown { Minimum = 0, Maximum = 99999 }));
            _valueBox.Value = Num(row, "value");
            top.Controls.Add(MakeField("Урон мин:", _minBox = new NumericUpDown { Minimum = 0, Maximum = 99999 }));
            _minBox.Value = Num(row, "damage_min");
            top.Controls.Add(MakeField("Урон макс:", _maxBox = new NumericUpDown { Minimum = 0, Maximum = 99999 }));
            _maxBox.Value = Num(row, "damage_max");
            top.Controls.Add(MakeField("Защита:", _defBox = new NumericUpDown { Minimum = 0, Maximum = 99999 }));
            _defBox.Value = Num(row, "defense");
            top.Controls.Add(MakeField("HP макс:", _hpBox = new NumericUpDown { Minimum = 0, Maximum = 99999 }));
            _hpBox.Value = Num(row, "max_health_bonus");
            top.Controls.Add(MakeField("Лечение:", _healBox = new NumericUpDown { Minimum = 0, Maximum = 99999 }));
            _healBox.Value = Num(row, "heal_amount");
            top.Controls.Add(MakeField("Сток:", _stockBox = new NumericUpDown { Minimum = 0, Maximum = 99999 }));
            _stockBox.Value = Num(row, "stock");
            top.Controls.Add(MakeField("Двуручная:", _thBox = new CheckBox()));
            _thBox.Checked = Num(row, "two_handed") != 0;

            _typeCombo.SelectedIndexChanged += (s, e) => UpdateFields();

            _descBox = new TextBox { Dock = DockStyle.Top, Height = 50, Multiline = true, ScrollBars = ScrollBars.Vertical };
            _descBox.Text = Cell(row, "description");

            var okBtn = new Button { Text = "Сохранить", Dock = DockStyle.Bottom, Height = 34, BackColor = SystemColors.ControlDark, FlatStyle = FlatStyle.Standard, Cursor = Cursors.Hand };
            okBtn.Click += (s, e) =>
            {
                SetCell(row, "id", _idBox.Text.Trim());
                SetCell(row, "name", _nameBox.Text.Trim());
                SetCell(row, "type", _typeCombo.SelectedItem?.ToString() ?? "");
                SetCell(row, "value", (int)_valueBox.Value);
                SetCell(row, "damage_min", (int)_minBox.Value);
                SetCell(row, "damage_max", (int)_maxBox.Value);
                SetCell(row, "defense", (int)_defBox.Value);
                SetCell(row, "max_health_bonus", (int)_hpBox.Value);
                SetCell(row, "heal_amount", (int)_healBox.Value);
                SetCell(row, "stock", (int)_stockBox.Value);
                SetCell(row, "two_handed", _thBox.Checked ? 1 : 0);
                SetCell(row, "description", _descBox.Text);
                DialogResult = DialogResult.OK;
                Close();
            };

            var cancelBtn = new Button { Text = "Отмена", Dock = DockStyle.Bottom, Height = 34, Margin = new Padding(0, 0, 0, 4) };
            cancelBtn.Click += (s, e) => DialogResult = DialogResult.Cancel;

            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            scroll.Controls.Add(top);

            Controls.Add(okBtn);
            Controls.Add(cancelBtn);
            Controls.Add(scroll);
            UpdateFields();
        }

        private TextBox _idBox = null!;
        private TextBox _nameBox = null!;
        private ComboBox _typeCombo = null!;
        private NumericUpDown _valueBox = null!;
        private NumericUpDown _minBox = null!;
        private NumericUpDown _maxBox = null!;
        private NumericUpDown _defBox = null!;
        private NumericUpDown _hpBox = null!;
        private NumericUpDown _healBox = null!;
        private NumericUpDown _stockBox = null!;
        private CheckBox _thBox = null!;
        private TextBox _descBox = null!;

        private string Cell(DataGridViewRow r, string col) => r.Cells[col]?.Value?.ToString() ?? "";
        private void SetCell(DataGridViewRow r, string col, object val) => r.Cells[col].Value = val;
        private int Num(DataGridViewRow r, string col) { var v = r.Cells[col]?.Value; return v == null || v is DBNull ? 0 : Convert.ToInt32(v); }

        private Panel MakeField(string labelText, Control ctrl)
        {
            var p = new Panel { Dock = DockStyle.Top, Height = 26 };
            var lbl = new Label { Text = labelText, Dock = DockStyle.Left, Width = 90, TextAlign = ContentAlignment.MiddleRight };
            ctrl.Dock = DockStyle.Fill;
            ctrl.Margin = new Padding(0);
            p.Controls.Add(ctrl);
            p.Controls.Add(lbl);
            return p;
        }

        private void UpdateFields()
        {
            string t = _typeCombo.SelectedItem?.ToString() ?? "";
            _minBox.Enabled = t is "weapon" or "twohand";
            _maxBox.Enabled = t is "weapon" or "twohand";
            _thBox.Enabled = t is "weapon" or "twohand";
            _defBox.Enabled = t is "shield" or "helmet" or "cloak" or "chest" or "legs" or "boots" or "glove" or "belt";
            _hpBox.Enabled = t is "shield" or "helmet" or "cloak" or "chest" or "legs" or "boots" or "glove" or "belt" or "consumable" or "trophy" or "weapon" or "twohand";
            _healBox.Enabled = t is "consumable" or "trophy" or "weapon" or "twohand";
        }
    }

    private void OpenDialogueEditor()
    {
        using var dlg = new NpcDialogueEditorForm(_dbFile);
        dlg.ShowDialog(this);
    }

    // === HELPERS ===

    private void SaveNpcsLocal(List<NpcRecord> npcs)
    {
        // Сохраняем существующие data (JSON диалогов), чтобы не затирать их при перезаписи.
        var dataMap = new Dictionary<string, string>();
        using (var readConn = new SqliteConnection($"Data Source={_dbFile}"))
        {
            readConn.Open();
            using var cmd = readConn.CreateCommand();
            cmd.CommandText = "SELECT id, data FROM npcs";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                if (!reader.IsDBNull(1)) dataMap[reader.GetString(0)] = reader.GetString(1);
            }
        }

        using var conn = new SqliteConnection($"Data Source={_dbFile}");
        conn.Open();
        using var transaction = conn.BeginTransaction();

        // Удаляем NPC, которых больше нет в таблице.
        if (npcs.Count == 0)
        {
            using (var del = conn.CreateCommand()) { del.CommandText = "DELETE FROM npcs"; del.ExecuteNonQuery(); }
        }
        else
        {
            var ids = string.Join(",", npcs.Select(n => "'" + n.Id.Replace("'", "''") + "'"));
            using var del = conn.CreateCommand();
            del.CommandText = $"DELETE FROM npcs WHERE id NOT IN ({ids})";
            del.ExecuteNonQuery();
        }

        foreach (var n in npcs)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"INSERT INTO npcs (id, name, type, data) VALUES ($id,$n,$t,$d)
                ON CONFLICT(id) DO UPDATE SET name = excluded.name, type = excluded.type";
            cmd.Parameters.AddWithValue("$id", n.Id);
            cmd.Parameters.AddWithValue("$n", n.Name);
            cmd.Parameters.AddWithValue("$t", n.Type);
            cmd.Parameters.AddWithValue("$d", dataMap.TryGetValue(n.Id, out var data) ? (object)data : DBNull.Value);
            cmd.ExecuteNonQuery();
        }
        transaction.Commit();
    }

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

    private string? GetMerchantNpcId()
    {
        using var conn = new SqliteConnection($"Data Source={_dbFile}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id FROM npcs WHERE type = 'merchant' LIMIT 1";
        return cmd.ExecuteScalar()?.ToString();
    }

    private static string NameById(List<(string Id, string Name)> refs, string id)
    {
        var found = refs.FirstOrDefault(r => r.Id == id);
        return found.Name ?? "";
    }

    private static string IdByName(List<(string Id, string Name)> refs, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        return refs.FirstOrDefault(r => r.Name == name).Id ?? "";
    }

    private static int ToInt(object? v) => int.TryParse(v?.ToString(), out int r) ? r : 0;
    private static double ToDouble(object? v) => double.TryParse(v?.ToString(), System.Globalization.CultureInfo.InvariantCulture, out double r) ? r : 0;
    private static string CellStr(DataGridViewRow row, string col) => row.Cells[col].Value?.ToString() ?? "";

    private void SetStatus(string text) => _status.Text = $"[{DateTime.Now:HH:mm:ss}] {text}";

    private class NpcRecord
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
    }
}

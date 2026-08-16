using System.Data;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using LostAndDivine.Shared;
using LostAndDivine.Shared.Migrations;
using LostAndDivine.Shared.Models;

namespace LostAndDivine.Editor;

public partial class MainForm : Form
{
    private readonly string _dbFile;
    private readonly string _contentDbFile;
    private TabControl _tabs = null!;
    private TabPage _itemsTab = null!;
    private TabPage _monstersTab = null!;
    private TabPage _questsTab = null!;
    private TabPage _worldTab = null!;
    private TabPage _merchantTab = null!;
    private TabPage _animTab = null!;
    private TabPage _accountsTab = null!;

    private DataGridView _itemsGrid = null!;
    private DataGridView _monstersGrid = null!;
    private DataGridView _questsGrid = null!;
    private DataGridView _worldGrid = null!;
    private Label _status = null!;

    private List<(string Id, string Name)> _monsterRefs = new();
    private List<(string Id, string Name)> _collectibleRefs = new();
    private List<(string Id, string Name)> _npcRefs = new();
    private List<(string Id, string Name)> _questRefs = new();
    private List<(string Id, string Name)> _rewardItemRefs = new();
    private Dictionary<string, string> _npcLocationByName = new();
    private Dictionary<string, string> _npcNameById = new();
    private Dictionary<(int, int), string> _npcPosToName = new();
    private Dictionary<string, string> _zoneNames = new();
    private Dictionary<string, string> _npcZoneByName = new();
    private int _worldWidth = 100;
    private int _worldHeight = 100;
    private ComboBox _itemTypeSelector = null!;
    private DataGridView _merchantGrid = null!;

    // Search boxes per tab
    private TextBox _itemsSearch = null!;
    private TextBox _monstersSearch = null!;
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
        _contentDbFile = Path.Combine(Path.GetDirectoryName(dbFile) ?? ".", "content.db");
        Text = "Редактор LostAndDivine — " + Path.GetFileName(dbFile);
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
        _itemTypeSelector.SelectedIndexChanged += (s, e) => ApplyItemsFilter();
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
        var monstersTop = new Panel { Dock = DockStyle.Top, Height = 58, Padding = new Padding(6, 4, 6, 4) };
        var monstersHint = new Label
        {
            Text = "Двойной клик по монстру — редактирование (включая дроп)",
            Dock = DockStyle.Top,
            Height = 22,
            TextAlign = ContentAlignment.MiddleLeft
        };
        _monstersSearch = MakeSearchBox("Поиск монстров...");
        _monstersSearch.Dock = DockStyle.Top;
        _monstersSearch.Height = 26;
        _monstersSearch.TextChanged += (s, e) => ApplyGridFilter(_monstersGrid, _monstersSearch.Text);
        monstersTop.Controls.Add(_monstersSearch);
        monstersTop.Controls.Add(monstersHint);
        _monstersGrid = MakeGrid();
        _monstersGrid.AllowUserToAddRows = true;
        _monstersGrid.AllowUserToDeleteRows = true;
        _monstersGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _monstersGrid.CellDoubleClick += (s, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= _monstersGrid.Rows.Count) return;
            if (_monstersGrid.Rows[e.RowIndex].IsNewRow) return;
            EditMonsterRow(_monstersGrid.Rows[e.RowIndex]);
        };
        monstersPanel.Controls.Add(_monstersGrid);
        monstersPanel.Controls.Add(monstersTop);
        var monstersBtn = MakeSaveButton("Сохранить монстров");
        monstersBtn.Click += (s, e) => SaveMonsters();
        monstersPanel.Controls.Add(monstersBtn);
        _monstersTab.Controls.Add(monstersPanel);

        // --- Квесты ---
        _questsTab = new TabPage("Квесты");
        var questsPanel = new Panel { Dock = DockStyle.Fill };
        var questsTop = new Panel { Dock = DockStyle.Top, Height = 32, Padding = new Padding(6, 4, 6, 4) };
        _questsSearch = MakeSearchBox("Поиск квестов...");
        _questsSearch.TextChanged += (s, e) => ApplyGridFilter(_questsGrid, _questsSearch.Text);
        questsTop.Controls.Add(_questsSearch);
        _questsGrid = MakeGrid();
        _questsGrid.AllowUserToAddRows = true;
        _questsGrid.AllowUserToDeleteRows = true;
        _questsGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _questsGrid.CellDoubleClick += (s, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= _questsGrid.Rows.Count) return;
            if (_questsGrid.Rows[e.RowIndex].IsNewRow) return;
            EditQuestRow(_questsGrid.Rows[e.RowIndex]);
        };
        questsPanel.Controls.Add(_questsGrid);
        questsPanel.Controls.Add(questsTop);
        var questsBtn = MakeSaveButton("Сохранить квесты");
        questsBtn.Click += (s, e) => SaveQuests();
        questsPanel.Controls.Add(questsBtn);
        _questsTab.Controls.Add(questsPanel);

        // --- Мир (NPC + размер карты) ---
        _worldTab = new TabPage("НПС");
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

        // --- Торговец (список NPC, которые могут торговать) ---
        _merchantTab = new TabPage("Торговец");
        var merchantPanel = new Panel { Dock = DockStyle.Fill };

        var merchantTop = new Panel { Dock = DockStyle.Top, Height = 34, Padding = new Padding(6, 4, 6, 4) };
        var merchantHint = new Label
        {
            Text = "Двойной клик по NPC — редактирование ассортимента",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };
        merchantTop.Controls.Add(merchantHint);

        _merchantGrid = MakeGrid();
        _merchantGrid.AllowUserToAddRows = false;
        _merchantGrid.AllowUserToDeleteRows = false;
        _merchantGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "id", HeaderText = "ID", DataPropertyName = "id", ReadOnly = true });
        _merchantGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "name", HeaderText = "Имя", DataPropertyName = "name", ReadOnly = true });
        _merchantGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "location", HeaderText = "Локация", DataPropertyName = "location", ReadOnly = true });
        _merchantGrid.CellDoubleClick += (s, e) =>
        {
            if (e.RowIndex < 0) return;
            var row = _merchantGrid.Rows[e.RowIndex];
            var id = row.Cells["id"].Value?.ToString() ?? "";
            var name = row.Cells["name"].Value?.ToString() ?? id;
            if (string.IsNullOrWhiteSpace(id)) return;
            using var f = new MerchantAssortmentEditorForm(_contentDbFile, id, name);
            f.ShowDialog(this);
        };

        merchantPanel.Controls.Add(_merchantGrid);
        merchantPanel.Controls.Add(merchantTop);
        _merchantTab.Controls.Add(merchantPanel);

        _tabs.TabPages.Add(_accountsTab = BuildAccountsTab());
        _tabs.TabPages.Add(_itemsTab);
        _tabs.TabPages.Add(_monstersTab);
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
            string escaped = search.Replace("'", "''");
            string sFilter = $"(name LIKE '%{escaped}%' OR id LIKE '%{escaped}%')";
            filter = string.IsNullOrEmpty(filter) ? sFilter : $"({filter}) AND {sFilter}";
        }
        dt.DefaultView.RowFilter = filter;
    }

    private void ApplyGridFilter(DataGridView grid, string searchText)
    {
        if (grid.DataSource is not DataTable dt) return;
        string search = GetSearchText(searchText);
        if (string.IsNullOrWhiteSpace(search)) { dt.DefaultView.RowFilter = ""; return; }
        string escaped = search.Replace("'", "''");
        var parts = new List<string>();
        foreach (DataColumn col in dt.Columns)
        {
            if (col.DataType == typeof(string))
                parts.Add($"{col.ColumnName} LIKE '%{escaped}%'");
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

    // === LOAD ALL ===

    private void LoadAll()
    {
        var connStr = $"Data Source={_dbFile}";
        DbMigrationRunner.RunMigrations(connStr);

        bool contentExisted = File.Exists(_contentDbFile);
        var contentConnStr = $"Data Source={_contentDbFile}";
        DbMigrationRunner.RunMigrations(contentConnStr);
        if (!contentExisted)
            ContentDbSeeder.CopyContentFromRuntimeIfNew(contentConnStr, _dbFile);

        LoadMonsterRefs();
        LoadCollectibleRefs();
        LoadNpcRefs();
        LoadZoneNames();
        BuildNpcZoneMapFromTiled();
        LoadQuestRefs();
        LoadRewardItemRefs();
        LoadItems();
        LoadMonsters();
        LoadQuests();
        LoadWorld();
        LoadMerchantNpcsGrid();
        LoadAccounts();
    }

    private void LoadMonsterRefs() => _monsterRefs = LoadRefs("SELECT id, name FROM monsters ORDER BY id");
    private void LoadCollectibleRefs() => _collectibleRefs = LoadRefs("SELECT id, name FROM items WHERE type='collectible' ORDER BY id");
    private void LoadNpcRefs()
    {
        _npcRefs = new List<(string, string)>();
        _npcLocationByName = new Dictionary<string, string>();
        _npcNameById = new Dictionary<string, string>();
        _npcPosToName = new Dictionary<(int, int), string>();
        using var conn = new SqliteConnection($"Data Source={_contentDbFile}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, location, x, y FROM npcs ORDER BY id";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            string name = reader.GetString(1);
            string loc = reader.IsDBNull(2) ? "" : reader.GetString(2);
            int x = reader.IsDBNull(3) ? 0 : reader.GetInt32(3);
            int y = reader.IsDBNull(4) ? 0 : reader.GetInt32(4);
            _npcRefs.Add((reader.GetString(0), name));
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
            using var conn = new SqliteConnection($"Data Source={_contentDbFile}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, name FROM zones ORDER BY id";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                _zoneNames[reader.GetString(0)] = reader.IsDBNull(1) ? reader.GetString(0) : reader.GetString(1);
        }
        catch { _zoneNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); }
    }

    /// <summary>
    /// Сопоставляет NPC из таблицы npcs с зоной, в которой они размещены на Tiled-картах
    /// (zone_{id}.tmj). Локация NPC = название зоны из Tiled, а не ручное поле.
    /// </summary>
    private void BuildNpcZoneMapFromTiled()
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
                zoneId = zoneId.Substring("zone_".Length);
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file));
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
                        // Сопоставляем NPC с объектом Tiled:
                        //  - по id (в Tiled name обычно id NPC, напр. N0003),
                        //  - по отображаемому имени,
                        //  - по координатам (tile), что работает даже если name — не id
                        //    (напр. у instance_portal name = зона назначения).
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

        // Перезаписываем локацию NPC по данным Tiled (приоритет у фактического размещения).
        foreach (var kvp in _npcZoneByName)
        {
            string loc = _zoneNames.TryGetValue(kvp.Value, out var zn) ? zn : kvp.Value;
            _npcLocationByName[kvp.Key] = loc;
        }
    }

    private IEnumerable<string> FindTiledZoneMaps()
    {
        var root = FindSolutionRoot(Path.GetDirectoryName(_contentDbFile) ?? ".");
        var found = new List<string>();
        if (!string.IsNullOrEmpty(root))
            ScanTiledMaps(new DirectoryInfo(root), found, 0, 6);
        return found;
    }

    private static string? FindSolutionRoot(string startDir)
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
    private void LoadQuestRefs() => _questRefs = LoadRefs("SELECT id, title FROM quests_def ORDER BY id");
    private void LoadRewardItemRefs() => _rewardItemRefs = LoadRefs("SELECT id, name FROM items WHERE type <> 'collectible' ORDER BY id");

    private List<(string Id, string Name)> LoadRefs(string query)
    {
        var list = new List<(string, string)>();
        using var conn = new SqliteConnection($"Data Source={_contentDbFile}");
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
        using var conn = new SqliteConnection($"Data Source={_contentDbFile}");
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
                values[i] = reader.IsDBNull(i) ? "" : string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}", reader.GetValue(i));
            dt.Rows.Add(values);
        }
        return dt;
    }

    // === ITEMS ===

    private void LoadItems()
    {
        var dt = LoadTable(@"SELECT id, name, type, value, damage_min, damage_max, defense, max_health_bonus, heal_amount, restore_mana, stock, description,
            bonus_strength, bonus_endurance, bonus_agility, bonus_cunning, bonus_intellect, bonus_wisdom,
            bonus_phys_attack, bonus_mag_attack, bonus_defense, bonus_resistance,
            bonus_attack_speed, bonus_crit_chance, bonus_crit_damage, bonus_evade_chance,
            two_handed, damage_type, attack_speed_modifier, weapon_subtype, attack_range, required_level,
            quest_item
            FROM items ORDER BY id");

        // LoadTable возвращает всё строками — для чекбокса нужен булев столбец
        var qcol = dt.Columns["quest_item"];
        if (qcol != null)
        {
            var boolCol = dt.Columns.Add("__qi", typeof(bool));
            foreach (DataRow r in dt.Rows)
                r["__qi"] = !r.IsNull(qcol) && Convert.ToString(r[qcol]) == "1";
            dt.Columns.Remove(qcol);
            boolCol.ColumnName = "quest_item";
        }

        _itemsGrid.DataSource = dt;
        SetupItemsTypeColumn();
        ShowOnlyIdNameType();
    }

    private void ShowOnlyIdNameType()
    {
        foreach (DataGridViewColumn col in _itemsGrid.Columns)
        {
            if (col.Name is "id" or "name" or "type" or "quest_item") continue;
            col.Visible = false;
        }
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

    // === MONSTERS ===

    private void LoadMonsters()
    {
        var drops = LoadMonsterDrops();
        var dt = LoadTable(@"SELECT id, name, tier, health, phys_attack, phys_defense, xp_reward, gold_reward, gold_max, symbol,
            strength, endurance, agility, cunning, intellect, wisdom, crit_chance, crit_damage, evade_chance, block_chance, parry_chance, shield_defense
            FROM monsters ORDER BY id");
        dt.Columns.Add("__drops", typeof(string));
        foreach (DataRow r in dt.Rows)
        {
            string monsterId = r["id"]?.ToString() ?? "";
            r["__drops"] = drops.TryGetValue(monsterId, out var list)
                ? JsonSerializer.Serialize(list.Select(d => new { d.ItemId, d.Chance }))
                : "[]";
        }
        _monstersGrid.DataSource = dt;
        foreach (DataGridViewColumn col in _monstersGrid.Columns)
        {
            if (col.Name is "id" or "name") continue;
            col.Visible = false;
        }
        if (_monstersGrid.Columns.Contains("id"))
            _monstersGrid.Columns["id"].HeaderText = "ID";
        if (_monstersGrid.Columns.Contains("name"))
            _monstersGrid.Columns["name"].HeaderText = "Имя";
        SetStatus($"Монстры: {dt.Rows.Count}");
    }

    /// <summary>Дропы монстров: monster_id → список (предмет, шанс %).</summary>
    private Dictionary<string, List<(string ItemId, int Chance)>> LoadMonsterDrops()
    {
        var map = new Dictionary<string, List<(string, int)>>();
        using var conn = new SqliteConnection($"Data Source={_contentDbFile}");
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
        dt.Columns.Add("use_item", typeof(string));
        dt.Columns.Add("npc", typeof(string));
        dt.Columns.Add("target_zone", typeof(string));
        dt.Columns.Add("target_x", typeof(string));
        dt.Columns.Add("target_y", typeof(string));
        dt.Columns.Add("target", typeof(string));
        dt.Columns.Add("xp_reward", typeof(string));
        dt.Columns.Add("gold_reward", typeof(string));
        dt.Columns.Add("chain_id", typeof(string));
        dt.Columns.Add("step", typeof(string));
        dt.Columns.Add("prereq", typeof(string));
        dt.Columns.Add("min_level", typeof(string));
        dt.Columns.Add("item_reward", typeof(string));
        dt.Columns.Add("item_reward_count", typeof(string));
        dt.Columns.Add("auto_grant", typeof(bool));
        dt.Columns.Add("giver_npc", typeof(string));
        dt.Columns.Add("is_story", typeof(bool));
        dt.Columns.Add("repeatable", typeof(bool));
        dt.Columns.Add("location", typeof(string));

        using var conn = new SqliteConnection($"Data Source={_contentDbFile}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT id, title, description, type, target_monster_id, target_item_id, target_npc_id, target,
                xp_reward, gold_reward, chain_id, step, prerequisite_quest_id, min_level, item_reward_id, item_reward_count,
                target_zone_id, target_x, target_y, auto_grant, giver_npc_id, is_story, location, repeatable
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
            string derivedLoc = !string.IsNullOrEmpty(gid) ? NpcLocationById(gid)
                : !string.IsNullOrEmpty(nid) ? NpcLocationById(nid)
                : (reader.IsDBNull(22) ? "" : reader.GetString(22));
            dt.Rows.Add(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                NameById(_monsterRefs, mid), NameById(_collectibleRefs, iid), NameById(_rewardItemRefs, iid),
                NameById(_npcRefs, nid),
                zone, reader.GetInt32(17).ToString(), reader.GetInt32(18).ToString(),
                reader.GetInt32(7).ToString(), reader.GetInt32(8).ToString(), reader.GetInt32(9).ToString(),
                ch, reader.GetInt32(11).ToString(), pr, reader.GetInt32(13).ToString(),
                NameById(_rewardItemRefs, rid), reader.GetInt32(15).ToString(),
                !reader.IsDBNull(19) && reader.GetInt32(19) != 0,
                NameById(_npcRefs, gid),
                !reader.IsDBNull(21) && reader.GetInt32(21) != 0,
                !reader.IsDBNull(23) ? reader.GetInt32(23) != 0 : false,
                derivedLoc);
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
        AddText("title", "Название квеста");
        _questsGrid.Columns.Add(new DataGridViewComboBoxColumn
        {
            DataPropertyName = "giver_npc", HeaderText = "NPC (выдаёт)", Name = "giver_npc",
            DataSource = _npcRefs.Select(r => r.Name).ToList()
        });
        _questsGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            DataPropertyName = "is_story", HeaderText = "Сюжетный", Name = "is_story"
        });
        _questsGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            DataPropertyName = "repeatable", HeaderText = "Повторяемый", Name = "repeatable"
        });
        AddText("location", "Локация");
        _questsGrid.Columns.Add(new DataGridViewComboBoxColumn
        {
            DataPropertyName = "type", HeaderText = "Тип", Name = "type",
            Items = { "kill", "collect", "talk", "travel", "use", "explore" }
        });
    }

    // === WORLD ===

    private void LoadWorld()
    {
        BuildWorldGrid();
        var dt = new DataTable();
        dt.Columns.Add("id", typeof(string));
        dt.Columns.Add("name", typeof(string));
        dt.Columns.Add("type", typeof(string));
        dt.Columns.Add("location", typeof(string));

        using var conn = new SqliteConnection($"Data Source={_contentDbFile}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, type, location FROM npcs ORDER BY id";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            string name = reader.GetString(1);
            // Локация берётся из Tiled-карты (зона размещения NPC), приоритетнее ручного поля.
            string loc = _npcZoneByName.TryGetValue(name, out var zid)
                ? (_zoneNames.TryGetValue(zid, out var zn) ? zn : zid)
                : (reader.IsDBNull(3) ? "" : reader.GetString(3));
            dt.Rows.Add(reader.GetString(0), name, reader.GetString(2), loc);
        }
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
        _worldGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "location", HeaderText = "Локация", DataPropertyName = "location" });
    }

    // === MERCHANT ===

    private void LoadMerchantNpcsGrid()
    {
        try
        {
            var dt = new DataTable();
            dt.Columns.Add("id", typeof(string));
            dt.Columns.Add("name", typeof(string));
            dt.Columns.Add("location", typeof(string));
            using var conn = new SqliteConnection($"Data Source={_contentDbFile}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, name FROM npcs WHERE type = 'merchant' ORDER BY id";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                string name = reader.GetString(1);
                dt.Rows.Add(reader.GetString(0), name,
                    _npcLocationByName.TryGetValue(name, out var loc) ? loc : "");
            }
            if (_merchantGrid.DataSource is DataTable old) old.Dispose();
            _merchantGrid.DataSource = dt;
            SetStatus("Торговцы загружены");
        }
        catch (Exception ex) { SetStatus("Ошибка (торговцы): " + ex.Message); }
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
        return Path.Combine(solRoot, "LostAndDivine.ClientMonoGame", "bin", "Debug", "net8.0", "Content");
    }

    private string ClientSrcContent()
    {
        var solRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        return Path.Combine(solRoot, "LostAndDivine.ClientMonoGame", "Content");
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
        else if (idx == _tabs.TabPages.IndexOf(_questsTab)) SaveQuests();
        else if (idx == _tabs.TabPages.IndexOf(_worldTab)) SaveWorld();
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
            using var conn = new SqliteConnection($"Data Source={_contentDbFile}");
            conn.Open();
            using var transaction = conn.BeginTransaction();
            using (var del = conn.CreateCommand()) { del.CommandText = "DELETE FROM items"; del.ExecuteNonQuery(); }
            foreach (DataRow row in dt.Rows)
            {
                if (row.RowState == DataRowState.Deleted) continue;
                if (string.IsNullOrWhiteSpace(row["id"]?.ToString())) continue;
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"INSERT INTO items (id, name, type, value, damage_min, damage_max, defense, max_health_bonus, heal_amount, restore_mana, stock, description,
                        bonus_strength, bonus_endurance, bonus_agility, bonus_cunning, bonus_intellect, bonus_wisdom,
                        bonus_phys_attack, bonus_mag_attack, bonus_defense, bonus_resistance,
                        bonus_attack_speed, bonus_crit_chance, bonus_crit_damage, bonus_evade_chance, two_handed,
                        damage_type, attack_speed_modifier, weapon_subtype, attack_range, required_level, quest_item)
                    VALUES ($id,$n,$t,$v,$dmn,$dmx,$d,$m,$h,$rm,$s,$desc,$str,$sta,$agi,$cun,$wis,$wil,$bpa,$bma,$bdef,$bres,$bas,$cc,$cd,$ec,$th,$dt,$asm,$ws,$ar,$rl,$qi)";
                cmd.Parameters.AddWithValue("$id", row["id"]);
                cmd.Parameters.AddWithValue("$n", row["name"] ?? "");
                cmd.Parameters.AddWithValue("$t", row["type"] ?? "");
                cmd.Parameters.AddWithValue("$v", ToInt(row["value"]));
                cmd.Parameters.AddWithValue("$dmn", ToInt(row["damage_min"]));
                cmd.Parameters.AddWithValue("$dmx", ToInt(row["damage_max"]));
                cmd.Parameters.AddWithValue("$d", ToInt(row["defense"]));
                cmd.Parameters.AddWithValue("$m", ToInt(row["max_health_bonus"]));
                cmd.Parameters.AddWithValue("$h", ToInt(row["heal_amount"]));
                cmd.Parameters.AddWithValue("$rm", ToInt(row["restore_mana"]));
                cmd.Parameters.AddWithValue("$s", ToInt(row["stock"]));
                cmd.Parameters.AddWithValue("$desc", row["description"] ?? "");
                cmd.Parameters.AddWithValue("$str", ToInt(row["bonus_strength"]));
                cmd.Parameters.AddWithValue("$sta", ToInt(row["bonus_endurance"]));
                cmd.Parameters.AddWithValue("$agi", ToInt(row["bonus_agility"]));
                cmd.Parameters.AddWithValue("$cun", ToInt(row["bonus_cunning"]));
                cmd.Parameters.AddWithValue("$wis", ToInt(row["bonus_intellect"]));
                cmd.Parameters.AddWithValue("$wil", ToInt(row["bonus_wisdom"]));
                cmd.Parameters.AddWithValue("$bpa", ToInt(row["bonus_phys_attack"]));
                cmd.Parameters.AddWithValue("$bma", ToInt(row["bonus_mag_attack"]));
                cmd.Parameters.AddWithValue("$bdef", ToInt(row["bonus_defense"]));
                cmd.Parameters.AddWithValue("$bres", ToInt(row["bonus_resistance"]));
                cmd.Parameters.AddWithValue("$bas", ToDouble(row["bonus_attack_speed"]));
                cmd.Parameters.AddWithValue("$cc", ToDouble(row["bonus_crit_chance"]));
                cmd.Parameters.AddWithValue("$cd", ToDouble(row["bonus_crit_damage"]));
                cmd.Parameters.AddWithValue("$ec", ToDouble(row["bonus_evade_chance"]));
                cmd.Parameters.AddWithValue("$th", ToInt(row["two_handed"]));
                cmd.Parameters.AddWithValue("$dt", row["damage_type"] ?? "");
                cmd.Parameters.AddWithValue("$asm", ToDouble(row["attack_speed_modifier"]));
                cmd.Parameters.AddWithValue("$ws", row["weapon_subtype"] ?? "");
                cmd.Parameters.AddWithValue("$ar", ToInt(row["attack_range"]));
                cmd.Parameters.AddWithValue("$rl", ToInt(row["required_level"]));
                cmd.Parameters.AddWithValue("$qi", QuestFlag(row["quest_item"]));
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
            using var conn = new SqliteConnection($"Data Source={_contentDbFile}");
            conn.Open();
            using var transaction = conn.BeginTransaction();
            using (var del = conn.CreateCommand()) { del.CommandText = "DELETE FROM monsters"; del.ExecuteNonQuery(); }
            using (var delDrops = conn.CreateCommand()) { delDrops.CommandText = "DELETE FROM monster_drops"; delDrops.ExecuteNonQuery(); }
            foreach (DataRow row in dt.Rows)
            {
                if (row.RowState == DataRowState.Deleted) continue;
                if (string.IsNullOrWhiteSpace(row["id"]?.ToString())) continue;
                string monsterId = row["id"].ToString()!;
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"INSERT INTO monsters (id, name, tier, health, phys_attack, phys_defense, xp_reward, gold_reward, gold_max, symbol,
                        strength, endurance, agility, cunning, intellect, wisdom, crit_chance, crit_damage, evade_chance,
                        block_chance, parry_chance, shield_defense)
                    VALUES ($id,$n,$t,$hp,$a,$d,$xp,$g,$gm,$s,$str,$sta,$agi,$cun,$wis,$wil,$cc,$cd,$ec,$bc,$pc,$sd)";
                cmd.Parameters.AddWithValue("$id", monsterId);
                cmd.Parameters.AddWithValue("$n", row["name"] ?? "");
                cmd.Parameters.AddWithValue("$t", ToInt(row["tier"]));
                cmd.Parameters.AddWithValue("$hp", ToInt(row["health"]));
                cmd.Parameters.AddWithValue("$a", ToInt(row["phys_attack"]));
                cmd.Parameters.AddWithValue("$d", ToInt(row["phys_defense"]));
                cmd.Parameters.AddWithValue("$xp", ToInt(row["xp_reward"]));
                cmd.Parameters.AddWithValue("$g", ToInt(row["gold_reward"]));
                cmd.Parameters.AddWithValue("$gm", ToInt(row["gold_max"]));
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
                cmd.Parameters.AddWithValue("$bc", ToDouble(row["block_chance"]));
                cmd.Parameters.AddWithValue("$pc", ToDouble(row["parry_chance"]));
                cmd.Parameters.AddWithValue("$sd", ToInt(row["shield_defense"]));
                cmd.ExecuteNonQuery();

                var dropEntries = ParseDrops(row["__drops"]?.ToString());
                foreach (var (itemId, chance) in dropEntries)
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
            LoadMonsters();
            SetStatus("Монстры сохранены");
        }
        catch (Exception ex) { SetStatus("Ошибка (монстры): " + ex.Message); }
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

    private void SaveQuests()
    {
        try
        {
            _questsGrid.EndEdit();
            var dt = (DataTable)_questsGrid.DataSource;
            EnsureId(dt, "Q");
            using var conn = new SqliteConnection($"Data Source={_contentDbFile}");
            conn.Open();
            using var transaction = conn.BeginTransaction();
            using (var del = conn.CreateCommand()) { del.CommandText = "DELETE FROM quests_def"; del.ExecuteNonQuery(); }
            foreach (DataRow row in dt.Rows)
            {
                if (row.RowState == DataRowState.Deleted) continue;
                if (string.IsNullOrWhiteSpace(row["id"]?.ToString())) continue;
                string type = row["type"]?.ToString() ?? "kill";
                string monsterId = type == "kill" ? IdByName(_monsterRefs, row["monster"]?.ToString() ?? "") : "";
                string itemId = type == "collect" ? IdByName(_collectibleRefs, row["item"]?.ToString() ?? "")
                    : type == "use" ? IdByName(_rewardItemRefs, row["use_item"]?.ToString() ?? "") : "";
                string npcId = IdByName(_npcRefs, row["npc"]?.ToString() ?? "");
                string giverId = IdByName(_npcRefs, row["giver_npc"]?.ToString() ?? "");
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"INSERT INTO quests_def (id, title, description, type, target_monster_id, target_item_id, target_npc_id, target, xp_reward, gold_reward, chain_id, step, prerequisite_quest_id, min_level, item_reward_id, item_reward_count, target_zone_id, target_x, target_y, auto_grant, giver_npc_id, is_story, location, repeatable)
                    VALUES ($id,$t,$d,$ty,$tm,$ti,$tn,$tg,$xp,$g,$ch,$st,$pr,$ml,$ri,$rc,$tz,$tx,$tyy,$ag,$gn,$is,$loc,$rep)";
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
                cmd.Parameters.AddWithValue("$tz", row["target_zone"] ?? "");
                cmd.Parameters.AddWithValue("$tx", ToInt(row["target_x"]));
                cmd.Parameters.AddWithValue("$tyy", ToInt(row["target_y"]));
                cmd.Parameters.AddWithValue("$ag", row["auto_grant"] is bool ag && ag ? 1 : 0);
                cmd.Parameters.AddWithValue("$gn", giverId);
                cmd.Parameters.AddWithValue("$is", row["is_story"] is bool ist && ist ? 1 : 0);
                cmd.Parameters.AddWithValue("$rep", row["is_story"] is bool iss && iss ? 0
                    : (row["repeatable"] is bool repb && repb ? 1 : 0));
                cmd.Parameters.AddWithValue("$loc", row["location"] ?? "");
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
                npcs.Add(new NpcRecord { Id = id, Name = name, Type = type, Location = CellStr(row, "location") });
            }
            SaveNpcsLocal(npcs);
            using (var conn = new SqliteConnection($"Data Source={_contentDbFile}"))
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
        string escaped = search.Replace("'", "''");
        dt.DefaultView.RowFilter = $"login LIKE '%{escaped}%' OR player_name LIKE '%{escaped}%'";
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
                cmd.Parameters.AddWithValue("$cz", dr["current_zone"] ?? BalanceStatic.MainZoneId);
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
            Text = $"Редактирование: {Cell(row, "name")}";
            Size = new Size(520, 760);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(8, 8, 8, 0) };

            // === ОСНОВНОЕ ===
            var grpMain = NewGroup(scroll, "Основное");
            _idBox = AddField(grpMain, "ID:", Cell(row, "id"));
            _nameBox = AddField(grpMain, "Название:", Cell(row, "name"));
            _typeCombo = AddCombo(grpMain, "Тип:", new[] { "weapon", "twohand", "shield", "helmet", "cloak", "chest", "legs", "boots", "glove", "belt", "necklace", "ring", "accessory", "consumable", "collectible", "trophy" }, Cell(row, "type"));
            _qualityCombo = AddCombo(grpMain, "Качество:", new[] { "Обычный", "Необычный", "Редкий", "Эпический" }, QualityFromDesc(Cell(row, "description")));
            _reqBox = AddNum(grpMain, "Треб.уровень:", Num(row, "required_level"), 0, 999);
            _valueBox = AddNum(grpMain, "Цена:", Num(row, "value"), 0, 999999);
            _stockBox = AddNum(grpMain, "Сток:", Num(row, "stock"), 0, 99999);

            // === ХАРАКТЕРИСТИКИ ===
            var grpStats = NewGroup(scroll, "Характеристики");
            _minBox = AddNum(grpStats, "Урон мин:", Num(row, "damage_min"), 0, 99999);
            _maxBox = AddNum(grpStats, "Урон макс:", Num(row, "damage_max"), 0, 99999);
            _defBox = AddNum(grpStats, "Защита:", Num(row, "defense"), 0, 99999);
            _hpBox = AddNum(grpStats, "HP:", Num(row, "max_health_bonus"), 0, 99999);
            _healBox = AddNum(grpStats, "Лечение:", Num(row, "heal_amount"), 0, 99999);
            _manaBox = AddNum(grpStats, "Восст.маны:", Num(row, "restore_mana"), 0, 99999);
            _thBox = AddCheck(grpStats, "Двуручное:", Num(row, "two_handed") != 0);

            // === БОНУСЫ К АТРИБУТАМ ===
            var grpAttr = NewGroup(scroll, "Бонусы к атрибутам");
            _strBox = AddNum(grpAttr, "Сила:", Num(row, "bonus_strength"), 0, 999);
            _endBox = AddNum(grpAttr, "Выносливость:", Num(row, "bonus_endurance"), 0, 999);
            _agiBox = AddNum(grpAttr, "Ловкость:", Num(row, "bonus_agility"), 0, 999);
            _cunBox = AddNum(grpAttr, "Хитрость:", Num(row, "bonus_cunning"), 0, 999);
            _intBox = AddNum(grpAttr, "Интеллект:", Num(row, "bonus_intellect"), 0, 999);
            _wisBox = AddNum(grpAttr, "Мудрость:", Num(row, "bonus_wisdom"), 0, 999);

            // === БОНУСЫ К ХАРАКТЕРИСТИКАМ ===
            var grpSec = NewGroup(scroll, "Бонусы к характеристикам");
            _bpaBox = AddNum(grpSec, "+Физ.атака:", Num(row, "bonus_phys_attack"), 0, 999);
            _bmaBox = AddNum(grpSec, "+Маг.атака:", Num(row, "bonus_mag_attack"), 0, 999);
            _bdefBox = AddNum(grpSec, "+Защита:", Num(row, "bonus_defense"), 0, 999);
            _bresBox = AddNum(grpSec, "+Сопротивл.:", Num(row, "bonus_resistance"), 0, 999);
            _basBox = AddNumDbl(grpSec, "+Скор.атаки:", (decimal)NumDbl(row, "bonus_attack_speed"), 0, 999, 1);
            _ccBox = AddNumDbl(grpSec, "+Крит.шанс (%):", (decimal)NumDbl(row, "bonus_crit_chance"), 0, 100, 1);
            _cdBox = AddNumDbl(grpSec, "+Крит.урон (%):", (decimal)NumDbl(row, "bonus_crit_damage"), 0, 999, 1);
            _ecBox = AddNumDbl(grpSec, "+Уклонение (%):", (decimal)NumDbl(row, "bonus_evade_chance"), 0, 100, 1);

            // === ОРУЖИЕ ===
            var grpWpn = NewGroup(scroll, "Оружие");
            _dmgTypeBox = AddCombo(grpWpn, "Тип урона:", new[] { "", "slashing", "piercing", "bludgeoning", "magic" }, Cell(row, "damage_type"));
            _subtypeBox = AddCombo(grpWpn, "Подтип:", new[] { "", "sword", "axe", "mace", "dagger", "greatsword", "poleaxe", "hammer", "greathammer", "halberd", "spear", "bow", "staff", "wand", "grimoire", "sphere", "shield" }, Cell(row, "weapon_subtype"));
            _asmBox = AddNumDbl(grpWpn, "Скор.атаки:", Math.Max(0.1M, (decimal)NumDbl(row, "attack_speed_modifier")), 0.1M, 5, 1, 0.1M);
            _arBox = AddNum(grpWpn, "Дальность:", Math.Max(1, Num(row, "attack_range")), 1, 10);

            // === ОПИСАНИЕ ===
            var grpDesc = NewGroup(scroll, "Описание");
            _descBox = new TextBox { Dock = DockStyle.Fill, Height = 60, Multiline = true, ScrollBars = ScrollBars.Vertical };
            grpDesc.Controls.Add(_descBox);
            _descBox.Text = Cell(row, "description");

            _typeCombo.SelectedIndexChanged += (s, e) => UpdateFields();

            // === КНОПКИ ===
            var btnPanel = new Panel { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(8, 8, 8, 0) };
            var cancelBtn = new Button { Text = "Отмена", Dock = DockStyle.Right, Width = 100, Height = 30 };
            cancelBtn.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            var okBtn = new Button { Text = "Сохранить", Dock = DockStyle.Right, Width = 100, Height = 30, BackColor = SystemColors.ControlDark, FlatStyle = FlatStyle.Standard, Cursor = Cursors.Hand, Margin = new Padding(0, 0, 8, 0) };
            okBtn.Click += (s, e) => SaveToRow(row);
            btnPanel.Controls.Add(cancelBtn);
            btnPanel.Controls.Add(okBtn);

            Controls.Add(btnPanel);
            Controls.Add(scroll);
            UpdateFields();
        }

        private TextBox _idBox = null!, _nameBox = null!, _descBox = null!;
        private ComboBox _typeCombo = null!, _qualityCombo = null!, _dmgTypeBox = null!, _subtypeBox = null!;
        private NumericUpDown _reqBox = null!, _valueBox = null!, _stockBox = null!;
        private NumericUpDown _minBox = null!, _maxBox = null!, _defBox = null!, _hpBox = null!, _healBox = null!, _manaBox = null!;
        private CheckBox _thBox = null!;
        private NumericUpDown _strBox = null!, _endBox = null!, _agiBox = null!, _cunBox = null!, _intBox = null!, _wisBox = null!;
        private NumericUpDown _bpaBox = null!, _bmaBox = null!, _bdefBox = null!, _bresBox = null!, _basBox = null!, _ccBox = null!, _cdBox = null!, _ecBox = null!;
        private NumericUpDown _asmBox = null!, _arBox = null!;

        private string Cell(DataGridViewRow r, string col) => r.Cells[col]?.Value?.ToString() ?? "";
        private void SetCell(DataGridViewRow r, string col, object val) => r.Cells[col].Value = val;
        private int Num(DataGridViewRow r, string col) { var v = r.Cells[col]?.Value; return v == null || v is DBNull ? 0 : Convert.ToInt32(v); }
        private double NumDbl(DataGridViewRow r, string col) { var v = r.Cells[col]?.Value; if (v == null || v is DBNull) return 0; return double.TryParse(v.ToString(), out var d) ? d : 0; }

        private static string QualityFromDesc(string desc)
        {
            if (desc.Contains("Эпический")) return "Эпический";
            if (desc.Contains("Редкий")) return "Редкий";
            if (desc.Contains("Необычный")) return "Необычный";
            return "Обычный";
        }

        private void SaveToRow(DataGridViewRow row)
        {
            SetCell(row, "id", _idBox.Text.Trim());
            SetCell(row, "name", _nameBox.Text.Trim());
            SetCell(row, "type", _typeCombo.SelectedItem?.ToString() ?? "");
            SetCell(row, "required_level", (int)_reqBox.Value);
            SetCell(row, "value", (int)_valueBox.Value);
            SetCell(row, "stock", (int)_stockBox.Value);
            SetCell(row, "damage_min", (int)_minBox.Value);
            SetCell(row, "damage_max", (int)_maxBox.Value);
            SetCell(row, "defense", (int)_defBox.Value);
            SetCell(row, "max_health_bonus", (int)_hpBox.Value);
            SetCell(row, "heal_amount", (int)_healBox.Value);
            SetCell(row, "restore_mana", (int)_manaBox.Value);
            SetCell(row, "two_handed", _thBox.Checked ? 1 : 0);
            SetCell(row, "bonus_strength", (int)_strBox.Value);
            SetCell(row, "bonus_endurance", (int)_endBox.Value);
            SetCell(row, "bonus_agility", (int)_agiBox.Value);
            SetCell(row, "bonus_cunning", (int)_cunBox.Value);
            SetCell(row, "bonus_intellect", (int)_intBox.Value);
            SetCell(row, "bonus_wisdom", (int)_wisBox.Value);
            SetCell(row, "bonus_phys_attack", (int)_bpaBox.Value);
            SetCell(row, "bonus_mag_attack", (int)_bmaBox.Value);
            SetCell(row, "bonus_defense", (int)_bdefBox.Value);
            SetCell(row, "bonus_resistance", (int)_bresBox.Value);
            SetCell(row, "bonus_attack_speed", (double)_basBox.Value);
            SetCell(row, "bonus_crit_chance", (double)_ccBox.Value);
            SetCell(row, "bonus_crit_damage", (double)_cdBox.Value);
            SetCell(row, "bonus_evade_chance", (double)_ecBox.Value);
            SetCell(row, "damage_type", _dmgTypeBox.Text);
            SetCell(row, "weapon_subtype", _subtypeBox.Text);
            SetCell(row, "attack_speed_modifier", (double)_asmBox.Value);
            SetCell(row, "attack_range", (int)_arBox.Value);
            string qLabel = _qualityCombo.SelectedItem?.ToString() ?? "Обычный";
            string cleanDesc = RemoveQualityFromDesc(_descBox.Text);
            SetCell(row, "description", $"Качество: {qLabel}. {cleanDesc}".TrimEnd('.', ' '));
            DialogResult = DialogResult.OK;
            Close();
        }

        private static string RemoveQualityFromDesc(string desc)
        {
            var idx = desc.IndexOf("Качество:");
            if (idx < 0) return desc;
            int dotIdx = desc.IndexOf(". ", idx);
            if (dotIdx < 0) return desc.Substring(0, idx).Trim();
            return (desc.Substring(0, idx) + desc.Substring(dotIdx + 2)).Trim();
        }

        // ---- Layout helpers ----

        private static GroupBox NewGroup(Panel parent, string title)
        {
            var g = new GroupBox
            {
                Text = title,
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(8, 18, 8, 6),
                Margin = new Padding(0, 0, 0, 4),
                MinimumSize = new Size(0, 0)
            };
            parent.Controls.Add(g);
            parent.Controls.SetChildIndex(g, 0);
            return g;
        }

        private static Panel AddRow(GroupBox g)
        {
            var row = new Panel { Height = 26, Dock = DockStyle.Top };
            g.Controls.Add(row);
            g.Controls.SetChildIndex(row, 0); // new rows appear at the top of the GroupBox
            return row;
        }

        private TextBox AddField(GroupBox g, string label, string value)
        {
            var row = AddRow(g);
            var ctrl = new TextBox { Text = value, Dock = DockStyle.Fill };
            var lbl = new Label { Text = label, Width = 115, Dock = DockStyle.Left, TextAlign = ContentAlignment.MiddleRight };
            row.Controls.Add(ctrl);
            row.Controls.Add(lbl);
            return ctrl;
        }

        private ComboBox AddCombo(GroupBox g, string label, string[] items, string current)
        {
            var row = AddRow(g);
            var ctrl = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            ctrl.Items.AddRange(items);
            ctrl.SelectedItem = items.Contains(current) ? current : items[0];
            var lbl = new Label { Text = label, Width = 115, Dock = DockStyle.Left, TextAlign = ContentAlignment.MiddleRight };
            row.Controls.Add(ctrl);
            row.Controls.Add(lbl);
            return ctrl;
        }

        private NumericUpDown AddNum(GroupBox g, string label, decimal val, decimal min, decimal max)
        {
            var row = AddRow(g);
            var ctrl = new NumericUpDown { Minimum = min, Maximum = max, Width = 100, Dock = DockStyle.Left };
            ctrl.Value = Math.Clamp(val, min, max);
            var lbl = new Label { Text = label, Width = 115, Dock = DockStyle.Left, TextAlign = ContentAlignment.MiddleRight };
            row.Controls.Add(ctrl);
            row.Controls.Add(lbl);
            row.Controls.SetChildIndex(ctrl, 0);
            return ctrl;
        }

        private NumericUpDown AddNumDbl(GroupBox g, string label, decimal val, decimal min, decimal max, int decPlaces, decimal inc = 1)
        {
            var num = AddNum(g, label, val, min, max);
            num.DecimalPlaces = decPlaces;
            num.Increment = inc;
            return num;
        }

        private CheckBox AddCheck(GroupBox g, string label, bool isChecked)
        {
            var row = AddRow(g);
            var ctrl = new CheckBox { Checked = isChecked, Dock = DockStyle.Fill };
            var lbl = new Label { Text = label, Width = 115, Dock = DockStyle.Left, TextAlign = ContentAlignment.MiddleRight };
            row.Controls.Add(ctrl);
            row.Controls.Add(lbl);
            return ctrl;
        }

        // ---- Context-sensitive enable/disable ----

        private void UpdateFields()
        {
            string t = _typeCombo.SelectedItem?.ToString() ?? "";
            bool isWeapon = t is "weapon" or "twohand";
            bool isArmor = t is "shield" or "helmet" or "cloak" or "chest" or "legs" or "boots" or "glove" or "belt";
            _minBox.Enabled = isWeapon;
            _maxBox.Enabled = isWeapon;
            _thBox.Enabled = isWeapon;
            _defBox.Enabled = isArmor;
            _hpBox.Enabled = isArmor || isWeapon || t is "consumable" or "trophy";
            _healBox.Enabled = t is "consumable" or "trophy" or "weapon" or "twohand";
            _manaBox.Enabled = t is "consumable";
            _dmgTypeBox.Enabled = isWeapon || t is "shield";
            _subtypeBox.Enabled = isWeapon || t is "shield";
            _asmBox.Enabled = isWeapon;
            _arBox.Enabled = isWeapon;
        }
    }

    private void EditMonsterRow(DataGridViewRow row)
    {
        using var dlg = new MonsterEditForm(row, this);
        if (dlg.ShowDialog() != DialogResult.OK) return;
        SetStatus("Монстр изменён");
    }

    internal class MonsterEditForm : Form
    {
        private readonly MainForm _owner;
        private readonly int _rowIndex;

        internal MonsterEditForm(DataGridViewRow row, MainForm owner) : base()
        {
            _owner = owner;
            _rowIndex = row.Index;
            Text = $"Монстр: {Cell(row, "name")} [{Cell(row, "id")}]";
            Size = new Size(560, 800);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(8, 8, 8, 0) };

            // === ОСНОВНОЕ ===
            var grpMain = NewGroup(scroll, "Основное");
            _idBox = AddField(grpMain, "ID:", Cell(row, "id"));
            _nameBox = AddField(grpMain, "Имя:", Cell(row, "name"));
            _tierBox = AddNum(grpMain, "Уровень:", Num(row, "tier"), 1, 99);
            _symbolBox = AddField(grpMain, "Символ:", Cell(row, "symbol"));

            // === ХАРАКТЕРИСТИКИ ===
            var grpStats = NewGroup(scroll, "Характеристики");
            _hpBox = AddNum(grpStats, "HP:", Num(row, "health"), 1, 999999999);
            _paBox = AddNum(grpStats, "Физ.атака:", Num(row, "phys_attack"), 0, 999999);
            _pdBox = AddNum(grpStats, "Физ.защита:", Num(row, "phys_defense"), 0, 999999);
            _xpBox = AddNum(grpStats, "Опыт за убийство:", Num(row, "xp_reward"), 0, 99999999);
            _goldBox = AddNum(grpStats, "Золото мин (за убийство):", Num(row, "gold_reward"), 0, 99999999);
            _goldMaxBox = AddNum(grpStats, "Золото макс (0 = без разброса):", Num(row, "gold_max"), 0, 99999999);

            // === АТРИБУТЫ ===
            var grpAttr = NewGroup(scroll, "Атрибуты");
            _strBox = AddNum(grpAttr, "Сила:", Num(row, "strength"), 0, 9999);
            _endBox = AddNum(grpAttr, "Выносливость:", Num(row, "endurance"), 0, 9999);
            _agiBox = AddNum(grpAttr, "Ловкость:", Num(row, "agility"), 0, 9999);
            _cunBox = AddNum(grpAttr, "Хитрость:", Num(row, "cunning"), 0, 9999);
            _intBox = AddNum(grpAttr, "Интеллект:", Num(row, "intellect"), 0, 9999);
            _wisBox = AddNum(grpAttr, "Мудрость:", Num(row, "wisdom"), 0, 9999);

            // === БОЙ ===
            var grpCombat = NewGroup(scroll, "Бой");
            _ccBox = AddNumDbl(grpCombat, "Крит.шанс (%):", (decimal)NumDbl(row, "crit_chance"), 0, 100, 1);
            _cdBox = AddNumDbl(grpCombat, "Крит.урон (%):", (decimal)NumDbl(row, "crit_damage"), 0, 1000, 1);
            _ecBox = AddNumDbl(grpCombat, "Уклонение (%):", (decimal)NumDbl(row, "evade_chance"), 0, 100, 1);
            _bcBox = AddNumDbl(grpCombat, "Блок (%):", (decimal)NumDbl(row, "block_chance"), 0, 100, 1);
            _pcBox = AddNumDbl(grpCombat, "Парирование (%):", (decimal)NumDbl(row, "parry_chance"), 0, 100, 1);
            _sdBox = AddNum(grpCombat, "Защита щитом:", Num(row, "shield_defense"), 0, 999999);

            // === ДРОП ===
            var grpDrops = NewGroup(scroll, "Дроп (предметы и шанс)");
            var hint = new Label
            {
                Text = "Добавьте строку в таблице, выберите предмет и укажите шанс выпадения (%).",
                Dock = DockStyle.Top,
                Height = 22,
                TextAlign = ContentAlignment.MiddleLeft
            };
            _dropsGrid = BuildDropsGrid();
            grpDrops.Controls.Add(_dropsGrid);
            grpDrops.Controls.Add(hint);
            LoadDrops(Cell(row, "__drops"));

            // === КНОПКИ ===
            var btnPanel = new Panel { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(8, 8, 8, 0) };
            var cancelBtn = new Button { Text = "Отмена", Dock = DockStyle.Right, Width = 100, Height = 30 };
            cancelBtn.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            var okBtn = new Button { Text = "Сохранить", Dock = DockStyle.Right, Width = 100, Height = 30, BackColor = SystemColors.ControlDark, FlatStyle = FlatStyle.Standard, Cursor = Cursors.Hand, Margin = new Padding(0, 0, 8, 0) };
            okBtn.Click += (s, e) => SaveToRow(row);
            btnPanel.Controls.Add(cancelBtn);
            btnPanel.Controls.Add(okBtn);

            Controls.Add(btnPanel);
            Controls.Add(scroll);
        }

        private TextBox _idBox = null!, _nameBox = null!, _symbolBox = null!;
        private NumericUpDown _tierBox = null!, _hpBox = null!, _paBox = null!, _pdBox = null!, _xpBox = null!, _goldBox = null!, _goldMaxBox = null!;
        private NumericUpDown _strBox = null!, _endBox = null!, _agiBox = null!, _cunBox = null!, _intBox = null!, _wisBox = null!;
        private NumericUpDown _ccBox = null!, _cdBox = null!, _ecBox = null!, _bcBox = null!, _pcBox = null!, _sdBox = null!;
        private DataGridView _dropsGrid = null!;

        private DataGridView BuildDropsGrid()
        {
            var grid = new DataGridView
            {
                Dock = DockStyle.Top,
                Height = 190,
                AllowUserToAddRows = true,
                AllowUserToDeleteRows = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                RowHeadersWidth = 30,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                BorderStyle = BorderStyle.FixedSingle
            };
            var itemNames = _owner.LoadRefs("SELECT id, name FROM items ORDER BY id");
            var itemCombo = new DataGridViewComboBoxColumn
            {
                Name = "item",
                HeaderText = "Предмет",
                DataSource = itemNames.Select(r => $"{r.Id} — {r.Name}").ToList(),
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
            };
            var chanceCol = new DataGridViewTextBoxColumn
            {
                Name = "chance",
                HeaderText = "Шанс, %",
                Width = 90,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
            };
            grid.Columns.Add(itemCombo);
            grid.Columns.Add(chanceCol);
            return grid;
        }

        private void LoadDrops(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || json == "[]") return;
            try
            {
                using var doc = JsonDocument.Parse(json);
                var itemNames = _owner.LoadRefs("SELECT id, name FROM items ORDER BY id")
                    .ToDictionary(r => r.Id, r => $"{r.Id} — {r.Name}");
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    string itemId = el.TryGetProperty("ItemId", out var idProp) ? idProp.GetString() ?? "" : "";
                    string chance = el.TryGetProperty("Chance", out var cProp) ? cProp.ToString() : "0";
                    if (string.IsNullOrWhiteSpace(itemId)) continue;
                    _dropsGrid.Rows.Add(itemNames.TryGetValue(itemId, out var display) ? display : itemId, chance);
                }
            }
            catch { }
        }

        private void SaveToRow(DataGridViewRow row)
        {
            SetCell(row, "id", _idBox.Text.Trim());
            SetCell(row, "name", _nameBox.Text.Trim());
            SetCell(row, "symbol", _symbolBox.Text.Trim());
            SetCell(row, "tier", (int)_tierBox.Value);
            SetCell(row, "health", (int)_hpBox.Value);
            SetCell(row, "phys_attack", (int)_paBox.Value);
            SetCell(row, "phys_defense", (int)_pdBox.Value);
            SetCell(row, "xp_reward", (int)_xpBox.Value);
            SetCell(row, "gold_reward", (int)_goldBox.Value);
            SetCell(row, "gold_max", (int)_goldMaxBox.Value);
            SetCell(row, "strength", (int)_strBox.Value);
            SetCell(row, "endurance", (int)_endBox.Value);
            SetCell(row, "agility", (int)_agiBox.Value);
            SetCell(row, "cunning", (int)_cunBox.Value);
            SetCell(row, "intellect", (int)_intBox.Value);
            SetCell(row, "wisdom", (int)_wisBox.Value);
            SetCell(row, "crit_chance", (double)_ccBox.Value);
            SetCell(row, "crit_damage", (double)_cdBox.Value);
            SetCell(row, "evade_chance", (double)_ecBox.Value);
            SetCell(row, "block_chance", (double)_bcBox.Value);
            SetCell(row, "parry_chance", (double)_pcBox.Value);
            SetCell(row, "shield_defense", (int)_sdBox.Value);

            _dropsGrid.EndEdit();
            var drops = new List<object>();
            foreach (DataGridViewRow r in _dropsGrid.Rows)
            {
                if (r.IsNewRow) continue;
                string value = r.Cells["item"].Value?.ToString() ?? "";
                string itemId = value.Contains(" — ") ? value.Substring(0, value.IndexOf(" — ", StringComparison.Ordinal)).Trim() : value.Trim();
                if (string.IsNullOrWhiteSpace(itemId)) continue;
                int chance = 0;
                int.TryParse(r.Cells["chance"].Value?.ToString(), out chance);
                drops.Add(new { ItemId = itemId, Chance = Math.Clamp(chance, 0, 100) });
            }
            SetCell(row, "__drops", JsonSerializer.Serialize(drops));
            DialogResult = DialogResult.OK;
            Close();
        }

        // ---- Layout helpers ----

        private static GroupBox NewGroup(Panel parent, string title)
        {
            var g = new GroupBox
            {
                Text = title,
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(8, 18, 8, 6),
                Margin = new Padding(0, 0, 0, 4),
                MinimumSize = new Size(0, 0)
            };
            parent.Controls.Add(g);
            parent.Controls.SetChildIndex(g, 0);
            return g;
        }

        private static Panel AddRow(GroupBox g)
        {
            var row = new Panel { Height = 26, Dock = DockStyle.Top };
            g.Controls.Add(row);
            g.Controls.SetChildIndex(row, 0);
            return row;
        }

        private TextBox AddField(GroupBox g, string label, string value)
        {
            var row = AddRow(g);
            var ctrl = new TextBox { Text = value, Dock = DockStyle.Fill };
            var lbl = new Label { Text = label, Width = 145, Dock = DockStyle.Left, TextAlign = ContentAlignment.MiddleRight };
            row.Controls.Add(ctrl);
            row.Controls.Add(lbl);
            return ctrl;
        }

        private NumericUpDown AddNum(GroupBox g, string label, decimal val, decimal min, decimal max)
        {
            var row = AddRow(g);
            var ctrl = new NumericUpDown { Minimum = min, Maximum = max, Width = 100, Dock = DockStyle.Left };
            ctrl.Value = Math.Clamp(val, min, max);
            var lbl = new Label { Text = label, Width = 145, Dock = DockStyle.Left, TextAlign = ContentAlignment.MiddleRight };
            row.Controls.Add(ctrl);
            row.Controls.Add(lbl);
            row.Controls.SetChildIndex(ctrl, 0);
            return ctrl;
        }

        private NumericUpDown AddNumDbl(GroupBox g, string label, decimal val, decimal min, decimal max, int decPlaces, decimal inc = 1)
        {
            var num = AddNum(g, label, val, min, max);
            num.DecimalPlaces = decPlaces;
            num.Increment = inc;
            return num;
        }

        private string Cell(DataGridViewRow r, string col) => r.Cells[col]?.Value?.ToString() ?? "";
        private void SetCell(DataGridViewRow r, string col, object val) => r.Cells[col].Value = val;
        private int Num(DataGridViewRow r, string col) { var v = r.Cells[col]?.Value; return v == null || v is DBNull ? 0 : Convert.ToInt32(v); }
        private double NumDbl(DataGridViewRow r, string col) { var v = r.Cells[col]?.Value; if (v == null || v is DBNull) return 0; return double.TryParse(v.ToString(), out var d) ? d : 0; }
    }

    private void EditQuestRow(DataGridViewRow row)
    {
        if (row.DataBoundItem is not DataRowView drv) return;
        using var dlg = new QuestEditForm(drv.Row, this);
        if (dlg.ShowDialog() != DialogResult.OK) return;
        SetStatus("Квест изменён");
    }

    internal class QuestEditForm : Form
    {
        private readonly MainForm _owner;

        internal QuestEditForm(DataRow row, MainForm owner) : base()
        {
            _owner = owner;
            Text = $"Квест: {Cell(row, "title")} [{Cell(row, "id")}]";
            Size = new Size(560, 820);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(8, 8, 8, 0) };

            // === ОСНОВНОЕ ===
            var grpMain = NewGroup(scroll, "Основное");
            _idBox = AddField(grpMain, "ID:", Cell(row, "id"));
            _titleBox = AddField(grpMain, "Название:", Cell(row, "title"));
            _typeCombo = AddCombo(grpMain, "Тип:", new[] { "kill", "collect", "talk", "travel", "use", "explore" }, Cell(row, "type"));
            _giverBox = AddComboList(grpMain, "NPC (выдаёт):", _owner._npcRefs.Select(r => r.Name).ToList(), Cell(row, "giver_npc"));
            _storyBox = AddCheck(grpMain, "Сюжетный:", Num(row, "is_story") != 0);
            _repBox = AddCheck(grpMain, "Повторяемый:", Num(row, "repeatable") != 0);
            _locBox = AddField(grpMain, "Локация:", Cell(row, "location"));
            // Подставим локацию выдатчика, если поле пустое.
            if (string.IsNullOrWhiteSpace(_locBox.Text))
                _locBox.Text = _owner._npcLocationByName.TryGetValue(_giverBox.Text, out var gl) ? gl : "";

            // === ОПИСАНИЕ ===
            var grpDesc = NewGroup(scroll, "Описание");
            _descBox = new TextBox { Dock = DockStyle.Fill, Height = 70, Multiline = true, ScrollBars = ScrollBars.Vertical };
            grpDesc.Controls.Add(_descBox);
            _descBox.Text = Cell(row, "description");

            // === ЦЕЛЬ ===
            var grpGoal = NewGroup(scroll, "Цель");
            _monsterBox = AddComboList(grpGoal, "Монстр (kill):", _owner._monsterRefs.Select(r => r.Name).ToList(), Cell(row, "monster"));
            _itemBox = AddComboList(grpGoal, "Предмет (collect):", _owner._collectibleRefs.Select(r => r.Name).ToList(), Cell(row, "item"));
            _useItemBox = AddComboList(grpGoal, "Предмет (use):", _owner._rewardItemRefs.Select(r => r.Name).ToList(), Cell(row, "use_item"));
            _npcBox = AddComboList(grpGoal, "NPC (talk/travel):", _owner._npcRefs.Select(r => r.Name).ToList(), Cell(row, "npc"));
            _zoneBox = AddField(grpGoal, "Зона (explore/авто):", Cell(row, "target_zone"));
            _xBox = AddNum(grpGoal, "Точка X (travel):", Num(row, "target_x"), -99999, 99999);
            _yBox = AddNum(grpGoal, "Точка Y (travel):", Num(row, "target_y"), -99999, 99999);
            _targetBox = AddNum(grpGoal, "Кол-во:", Num(row, "target"), 0, 999999);

            // === УСЛОВИЯ ===
            var grpCond = NewGroup(scroll, "Условия и выдача");
            _autoBox = AddCheck(grpCond, "Авто-выдача при входе в зону:", Num(row, "auto_grant") != 0);
            _minLvlBox = AddNum(grpCond, "Мин. уровень:", Num(row, "min_level"), 0, 999);
            _chainBox = AddField(grpCond, "Цепочка (ID):", Cell(row, "chain_id"));
            _stepBox = AddNum(grpCond, "Шаг в цепочке:", Num(row, "step"), 0, 9999);
            var prereqNames = new List<string> { "" };
            prereqNames.AddRange(_owner._questRefs.Select(r => r.Name));
            _prereqBox = AddComboList(grpCond, "Предусловие (квест):", prereqNames, Cell(row, "prereq"));

            // === НАГРАДЫ ===
            var grpReward = NewGroup(scroll, "Награды");
            _xpBox = AddNum(grpReward, "Опыт:", Num(row, "xp_reward"), 0, 99999999);
            _goldBox = AddNum(grpReward, "Золото:", Num(row, "gold_reward"), 0, 99999999);
            _itemRewardBox = AddComboList(grpReward, "Награда (предмет):", _owner._rewardItemRefs.Select(r => r.Name).ToList(), Cell(row, "item_reward"));
            _itemRewardCountBox = AddNum(grpReward, "Награда (кол-во):", Num(row, "item_reward_count"), 0, 99999);

            // === ДИАЛОГИ (принадлежат NPC-выдатчику) ===
            var grpDlg = NewGroup(scroll, "Диалоги (NPC-выдатчик)");
            _dlgBtn = new Button { Text = "Открыть диалог NPC-выдатчика...", Dock = DockStyle.Fill, Height = 28, Cursor = Cursors.Hand };
            _dlgBtn.Click += (s, e) => OpenGiverDialogue();
            var dlgRow = AddRow(grpDlg);
            dlgRow.Controls.Add(_dlgBtn);

            _typeCombo.SelectedIndexChanged += (s, e) => UpdateFields();
            _storyBox.CheckedChanged += (s, e) =>
            {
                // Для сюжетных квестов параметр «повторяемый» не используется.
                _repBox.Enabled = !_storyBox.Checked;
                if (_storyBox.Checked) _repBox.Checked = false;
            };
            _repBox.Enabled = !_storyBox.Checked;
            _giverBox.SelectedIndexChanged += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(_locBox.Text) || _locBox.Text == _lastGiverLoc)
                    _locBox.Text = _owner._npcLocationByName.TryGetValue(_giverBox.Text, out var l) ? l : "";
                _lastGiverLoc = _owner._npcLocationByName.TryGetValue(_giverBox.Text, out var nl) ? nl : "";
            };
            _lastGiverLoc = _owner._npcLocationByName.TryGetValue(_giverBox.Text, out var init) ? init : "";

            // === КНОПКИ ===
            var btnPanel = new Panel { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(8, 8, 8, 0) };
            var cancelBtn = new Button { Text = "Отмена", Dock = DockStyle.Right, Width = 100, Height = 30 };
            cancelBtn.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            var okBtn = new Button { Text = "Сохранить", Dock = DockStyle.Right, Width = 100, Height = 30, BackColor = SystemColors.ControlDark, FlatStyle = FlatStyle.Standard, Cursor = Cursors.Hand, Margin = new Padding(0, 0, 8, 0) };
            okBtn.Click += (s, e) => SaveToRow(row);
            btnPanel.Controls.Add(cancelBtn);
            btnPanel.Controls.Add(okBtn);

            Controls.Add(btnPanel);
            Controls.Add(scroll);
            UpdateFields();
        }

        private TextBox _idBox = null!, _titleBox = null!, _descBox = null!, _locBox = null!, _zoneBox = null!, _chainBox = null!;
        private ComboBox _giverBox = null!, _monsterBox = null!, _itemBox = null!, _useItemBox = null!, _npcBox = null!, _prereqBox = null!, _itemRewardBox = null!;
        private ComboBox _typeCombo = null!;
        private NumericUpDown _xBox = null!, _yBox = null!, _targetBox = null!, _minLvlBox = null!, _stepBox = null!, _xpBox = null!, _goldBox = null!, _itemRewardCountBox = null!;
        private CheckBox _storyBox = null!, _autoBox = null!, _repBox = null!;
        private Button _dlgBtn = null!;
        private string _lastGiverLoc = "";

        private string Cell(DataRow r, string col) => r[col]?.ToString() ?? "";
        private void SetCell(DataRow r, string col, object val) => r[col] = val;
        private int Num(DataRow r, string col) { var v = r[col]; return v == null || v is DBNull ? 0 : Convert.ToInt32(v); }

        private void OpenGiverDialogue()
        {
            string npcName = _giverBox.Text;
            var npc = _owner._npcRefs.FirstOrDefault(r => r.Name == npcName);
            if (string.IsNullOrEmpty(npc.Id))
            {
                MessageBox.Show("Сначала выберите NPC-выдатчика.", "Диалоги", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using var dlg = new NpcDialogueEditorForm(_owner._contentDbFile, npc.Id);
            dlg.ShowDialog(this);
        }

        private void SaveToRow(DataRow row)
        {
            SetCell(row, "id", _idBox.Text.Trim());
            SetCell(row, "title", _titleBox.Text.Trim());
            SetCell(row, "type", _typeCombo.SelectedItem?.ToString() ?? "kill");
            SetCell(row, "giver_npc", _giverBox.Text);
            SetCell(row, "is_story", _storyBox.Checked);
            SetCell(row, "repeatable", _repBox.Checked);
            SetCell(row, "location", _locBox.Text.Trim());
            SetCell(row, "description", _descBox.Text);
            SetCell(row, "monster", _monsterBox.Text);
            SetCell(row, "item", _itemBox.Text);
            SetCell(row, "use_item", _useItemBox.Text);
            SetCell(row, "npc", _npcBox.Text);
            SetCell(row, "target_zone", _zoneBox.Text.Trim());
            SetCell(row, "target_x", (int)_xBox.Value);
            SetCell(row, "target_y", (int)_yBox.Value);
            SetCell(row, "target", (int)_targetBox.Value);
            SetCell(row, "auto_grant", _autoBox.Checked);
            SetCell(row, "min_level", (int)_minLvlBox.Value);
            SetCell(row, "chain_id", _chainBox.Text.Trim());
            SetCell(row, "step", (int)_stepBox.Value);
            SetCell(row, "prereq", _prereqBox.Text);
            SetCell(row, "xp_reward", (int)_xpBox.Value);
            SetCell(row, "gold_reward", (int)_goldBox.Value);
            SetCell(row, "item_reward", _itemRewardBox.Text);
            SetCell(row, "item_reward_count", (int)_itemRewardCountBox.Value);
            DialogResult = DialogResult.OK;
            Close();
        }

        private void UpdateFields()
        {
            string t = _typeCombo.SelectedItem?.ToString() ?? "";
            _monsterBox.Enabled = t == "kill";
            _itemBox.Enabled = t == "collect";
            _useItemBox.Enabled = t == "use";
            _npcBox.Enabled = t is "talk" or "travel";
            _zoneBox.Enabled = t is "explore" or "travel";
            _xBox.Enabled = t == "travel";
            _yBox.Enabled = t == "travel";
            _targetBox.Enabled = t is "kill" or "collect" or "use" or "talk";
        }

        // ---- Layout helpers ----

        private static GroupBox NewGroup(Panel parent, string title)
        {
            var g = new GroupBox
            {
                Text = title,
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(8, 18, 8, 6),
                Margin = new Padding(0, 0, 0, 4)
            };
            parent.Controls.Add(g);
            parent.Controls.SetChildIndex(g, 0);
            return g;
        }

        private static Panel AddRow(GroupBox g)
        {
            var row = new Panel { Height = 26, Dock = DockStyle.Top };
            g.Controls.Add(row);
            g.Controls.SetChildIndex(row, 0);
            return row;
        }

        private TextBox AddField(GroupBox g, string label, string value)
        {
            var row = AddRow(g);
            var ctrl = new TextBox { Text = value, Dock = DockStyle.Fill };
            var lbl = new Label { Text = label, Width = 130, Dock = DockStyle.Left, TextAlign = ContentAlignment.MiddleRight };
            row.Controls.Add(ctrl);
            row.Controls.Add(lbl);
            return ctrl;
        }

        private ComboBox AddCombo(GroupBox g, string label, string[] items, string current)
        {
            return AddComboList(g, label, items.ToList(), current);
        }

        private ComboBox AddComboList(GroupBox g, string label, List<string> items, string current)
        {
            var row = AddRow(g);
            var ctrl = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            ctrl.Items.AddRange(items.ToArray());
            ctrl.SelectedItem = items.Contains(current) ? current : (items.Count > 0 ? items[0] : null);
            var lbl = new Label { Text = label, Width = 130, Dock = DockStyle.Left, TextAlign = ContentAlignment.MiddleRight };
            row.Controls.Add(ctrl);
            row.Controls.Add(lbl);
            return ctrl;
        }

        private NumericUpDown AddNum(GroupBox g, string label, decimal val, decimal min, decimal max)
        {
            var row = AddRow(g);
            var ctrl = new NumericUpDown { Minimum = min, Maximum = max, Width = 120, Dock = DockStyle.Left };
            ctrl.Value = Math.Clamp(val, min, max);
            var lbl = new Label { Text = label, Width = 130, Dock = DockStyle.Left, TextAlign = ContentAlignment.MiddleRight };
            row.Controls.Add(ctrl);
            row.Controls.Add(lbl);
            row.Controls.SetChildIndex(ctrl, 0);
            return ctrl;
        }

        private CheckBox AddCheck(GroupBox g, string label, bool isChecked)
        {
            var row = AddRow(g);
            var ctrl = new CheckBox { Checked = isChecked, Dock = DockStyle.Fill };
            var lbl = new Label { Text = label, Width = 130, Dock = DockStyle.Left, TextAlign = ContentAlignment.MiddleRight };
            row.Controls.Add(ctrl);
            row.Controls.Add(lbl);
            return ctrl;
        }
    }

    private void OpenDialogueEditor()
    {
        using var dlg = new NpcDialogueEditorForm(_contentDbFile);
        dlg.ShowDialog(this);
    }

    // === HELPERS ===

    private void SaveNpcsLocal(List<NpcRecord> npcs)
    {
        // Сохраняем существующие data (JSON диалогов), чтобы не затирать их при перезаписи.
        var dataMap = new Dictionary<string, string>();
        using (var readConn = new SqliteConnection($"Data Source={_contentDbFile}"))
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

        using var conn = new SqliteConnection($"Data Source={_contentDbFile}");
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
            cmd.CommandText = @"INSERT INTO npcs (id, name, type, location, data) VALUES ($id,$n,$t,$l,$d)
                ON CONFLICT(id) DO UPDATE SET name = excluded.name, type = excluded.type, location = excluded.location";
            cmd.Parameters.AddWithValue("$id", n.Id);
            cmd.Parameters.AddWithValue("$n", n.Name);
            cmd.Parameters.AddWithValue("$t", n.Type);
            cmd.Parameters.AddWithValue("$l", n.Location ?? "");
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
            using var conn = new SqliteConnection($"Data Source={_contentDbFile}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT value FROM world_config WHERE key = $k";
            cmd.Parameters.AddWithValue("$k", key);
            var v = cmd.ExecuteScalar();
            return v == null ? defaultValue : Convert.ToInt32(v);
        }
        catch { return defaultValue; }
    }

    private static string NameById(List<(string Id, string Name)> refs, string id)
    {
        var found = refs.FirstOrDefault(r => r.Id == id);
        return found.Name ?? "";
    }

    private string NpcLocationById(string npcId)
    {
        if (string.IsNullOrEmpty(npcId)) return "";
        if (_npcNameById.TryGetValue(npcId, out var name) && _npcLocationByName.TryGetValue(name, out var loc)) return loc;
        return "";
    }

    private static string IdByName(List<(string Id, string Name)> refs, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        return refs.FirstOrDefault(r => r.Name == name).Id ?? "";
    }

    private static int ToInt(object? v) => int.TryParse(v?.ToString(), out int r) ? r : 0;
    private static int QuestFlag(object? v) => v is bool b ? (b ? 1 : 0) : ToInt(v);
    private static double ToDouble(object? v) => double.TryParse(v?.ToString(), System.Globalization.CultureInfo.InvariantCulture, out double r) ? r : 0;
    private static string CellStr(DataGridViewRow row, string col) => row.Cells[col].Value?.ToString() ?? "";

    private void SetStatus(string text) => _status.Text = $"[{DateTime.Now:HH:mm:ss}] {text}";

    private class NpcRecord
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public string Location { get; set; } = "";
    }
}

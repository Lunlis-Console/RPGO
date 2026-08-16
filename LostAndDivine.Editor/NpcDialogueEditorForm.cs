using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using LostAndDivine.Shared.Models;

namespace LostAndDivine.Editor;

/// <summary>
/// Редактор диалогов NPC: дерево узлов, реплики, варианты ответов (текст/next/action/condition).
/// Сохраняет результат в столбец data таблицы npcs (JSON в формате DialogueParser).
/// </summary>
public class NpcDialogueEditorForm : Form
{
    private readonly string _dbFile;

    private ComboBox _npcSelector = null!;
    private TextBox _nodeKeyBox = null!;
    private TreeView _nodeTree = null!;
    private TextBox _speakerBox = null!;
    private TextBox _textBox = null!;
    private DataGridView _choicesGrid = null!;
    private Label _status = null!;
    private SplitContainer _split = null!;

    private readonly List<(string Id, string Name, string Type)> _npcs = new();
    private DialogueTree? _tree;
    private string _currentNpcId = "";

    public NpcDialogueEditorForm(string dbFile, string? presetNpcId = null)
    {
        _dbFile = dbFile;
        Text = "Редактор диалогов NPC";
        Size = new Size(980, 640);
        MinimumSize = new Size(760, 480);
        StartPosition = FormStartPosition.CenterParent;
        BuildUI();
        LoadNpcs();
        if (!string.IsNullOrEmpty(presetNpcId))
        {
            int idx = _npcs.FindIndex(n => string.Equals(n.Id, presetNpcId, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) _npcSelector.SelectedIndex = idx;
        }
    }

    private void BuildUI()
    {
        var top = new Panel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(6, 6, 6, 4) };
        var npcLabel = new Label { Text = "NPC:", Dock = DockStyle.Left, Width = 42, TextAlign = ContentAlignment.MiddleLeft };
        _npcSelector = new ComboBox { Dock = DockStyle.Left, Width = 320, DropDownStyle = ComboBoxStyle.DropDownList };
        _npcSelector.SelectedIndexChanged += (s, e) => LoadDialogueForSelected();
        top.Controls.Add(npcLabel);
        top.Controls.Add(_npcSelector);

        var nodePanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6) };

        var nodeButtons = new Panel { Dock = DockStyle.Top, Height = 34, Padding = new Padding(0, 0, 0, 4) };
        var addNodeBtn = MakeButton("Добавить узел");
        addNodeBtn.Click += (s, e) => AddNode();
        var delNodeBtn = MakeButton("Удалить узел");
        delNodeBtn.Click += (s, e) => DeleteNode();
        var renameBtn = MakeButton("Переименовать");
        renameBtn.Click += (s, e) => RenameNode();
        nodeButtons.Controls.Add(renameBtn);
        nodeButtons.Controls.Add(delNodeBtn);
        nodeButtons.Controls.Add(addNodeBtn);

        _nodeKeyBox = new TextBox { Dock = DockStyle.Top, Height = 28, BorderStyle = BorderStyle.FixedSingle, PlaceholderText = "greeting (ключ узла)" };
        _nodeTree = new TreeView { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 10) };
        _nodeTree.AfterSelect += (s, e) => LoadNodeEditor();
        _nodeTree.AfterLabelEdit += (s, e) => { if (!string.IsNullOrWhiteSpace(e.Label)) RenameNodeTo(e.Label); };

        nodePanel.Controls.Add(_nodeTree);
        nodePanel.Controls.Add(_nodeKeyBox);
        nodePanel.Controls.Add(nodeButtons);

        _split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 360};

        _split.Panel1.Controls.Add(nodePanel);

        var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6) };

        var fields = new Panel { Dock = DockStyle.Top, Height = 184, Padding = new Padding(0, 0, 0, 6) };
        _speakerBox = new TextBox { Dock = DockStyle.Fill };
        _textBox = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical };
        _speakerBox.TextChanged += (s, e) => ApplyNodeEdit();
        _textBox.TextChanged += (s, e) => ApplyNodeEdit();
        fields.Controls.Add(MakeField("Реплика:", _textBox, 140));
        fields.Controls.Add(MakeField("Кто говорит:", _speakerBox, 28));

        _choicesGrid = MakeGrid();
        _choicesGrid.CellEndEdit += (s, e) => ApplyChoiceEdit();

        right.Controls.Add(_choicesGrid);
        right.Controls.Add(fields);
        _split.Panel2.Controls.Add(right);

        // Fill-контрол добавляем первым, чтобы панели Top/Bottom не перекрывались сплитом.
        Controls.Add(_split);
        Controls.Add(top);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 42, Padding = new Padding(6) };
        _status = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Text = "Выберите NPC" };
        var saveBtn = new Button
        {
            Text = "Сохранить диалог",
            Dock = DockStyle.Right,
            Width = 160,
            Height = 30,
            BackColor = SystemColors.ControlDark,
            ForeColor = SystemColors.WindowText,
            FlatStyle = FlatStyle.Standard,
            Cursor = Cursors.Hand};
        saveBtn.Click += (s, e) => SaveDialogue();
        bottom.Controls.Add(_status);
        bottom.Controls.Add(saveBtn);
        Controls.Add(bottom);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        // После автоматического масштабирования шрифта/DPI SplitterDistance мог разъехаться
        // (правая панель сжалась бы до ~180px). Фиксируем корректную ширину левой панели.
        _split.SplitterDistance = Math.Min(380, Math.Max(300, ClientSize.Width - 420));
    }

    private void LoadNpcs()
    {
        _npcs.Clear();
        using var conn = new SqliteConnection($"Data Source={_dbFile}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, type FROM npcs ORDER BY id";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            _npcs.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        foreach (var n in _npcs)
            _npcSelector.Items.Add($"{n.Id} — {n.Name} [{n.Type}]");
        if (_npcs.Count > 0) _npcSelector.SelectedIndex = 0;
    }

    private void LoadDialogueForSelected()
    {
        _currentNpcId = _npcSelector.SelectedIndex >= 0 && _npcSelector.SelectedIndex < _npcs.Count
            ? _npcs[_npcSelector.SelectedIndex].Id
            : "";
        _tree = null;

        string? data = null;
        using (var conn = new SqliteConnection($"Data Source={_dbFile}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT data FROM npcs WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", _currentNpcId);
            var v = cmd.ExecuteScalar();
            data = v?.ToString();
        }

        _tree = DialogueParser.Parse(data);
        if (_tree == null)
        {
            _tree = new DialogueTree();
            _tree.Nodes["greeting"] = new DialogueNode { Speaker = _npcName(), Text = "" };
        }

        RefreshTree();
        if (_tree.Nodes.Count > 0 && _nodeTree.Nodes.Count > 0)
            _nodeTree.SelectedNode = _nodeTree.Nodes[0];
        _status.Text = $"Узлов: {_tree.Nodes.Count} — редактирование '{_currentNpcId}'";
    }

    private string _npcName()
    {
        if (_npcSelector.SelectedIndex >= 0 && _npcSelector.SelectedIndex < _npcs.Count)
            return _npcs[_npcSelector.SelectedIndex].Name;
        return "";
    }

    private void RefreshTree()
    {
        _nodeTree.BeginUpdate();
        _nodeTree.Nodes.Clear();
        if (_tree != null)
        {
            foreach (var kvp in _tree.Nodes)
            {
                var node = _nodeTree.Nodes.Add(kvp.Key);
                node.Text = kvp.Key + (string.IsNullOrWhiteSpace(kvp.Value.Text) ? "" : " — " + Truncate(kvp.Value.Text, 40));
                node.Tag = kvp.Key;
            }
        }
        _nodeTree.EndUpdate();
    }

    private string? SelectedNodeKey() => _nodeTree.SelectedNode?.Tag?.ToString();

    private void LoadNodeEditor()
    {
        if (_tree == null) return;
        string? key = SelectedNodeKey();
        if (key == null || !_tree.Nodes.TryGetValue(key, out var node))
        {
            _speakerBox.Text = "";
            _textBox.Text = "";
            _nodeKeyBox.Text = "";
            _choicesGrid.DataSource = null;
            return;
        }
        _nodeKeyBox.Text = key;
        _speakerBox.Text = node.Speaker;
        _textBox.Text = node.Text;
        BindChoices(key);
    }

    private void BindChoices(string nodeKey)
    {
        var dt = new System.Data.DataTable();
        dt.Columns.Add("text", typeof(string));
        dt.Columns.Add("next", typeof(string));
        dt.Columns.Add("action", typeof(string));
        dt.Columns.Add("condition", typeof(string));
        if (_tree!.Nodes.TryGetValue(nodeKey, out var node))
        {
            foreach (var c in node.Choices)
                dt.Rows.Add(c.Text, c.NextNodeId ?? "", c.Action ?? "", c.Condition ?? "");
        }
        _choicesGrid.DataSource = dt;
        _choicesGrid.AllowUserToAddRows = true;
        _choicesGrid.AllowUserToDeleteRows = true;
        foreach (DataGridViewColumn col in _choicesGrid.Columns)
            col.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        _choicesGrid.Columns["text"].HeaderText = "Текст ответа";
        _choicesGrid.Columns["next"].HeaderText = "Следующий узел";
        _choicesGrid.Columns["action"].HeaderText = "Действие";
        _choicesGrid.Columns["condition"].HeaderText = "Условие";
    }

    private void ApplyNodeEdit()
    {
        if (_tree == null) return;
        string? key = SelectedNodeKey();
        if (key == null || !_tree.Nodes.TryGetValue(key, out var node)) return;
        node.Speaker = _speakerBox.Text;
        node.Text = _textBox.Text;
        var tn = _nodeTree.SelectedNode;
        if (tn != null) tn.Text = key + (string.IsNullOrWhiteSpace(_textBox.Text) ? "" : " — " + Truncate(_textBox.Text, 40));
    }

    private void ApplyChoiceEdit()
    {
        if (_tree == null || _choicesGrid.DataSource is not System.Data.DataTable dt) return;
        string? key = SelectedNodeKey();
        if (key == null || !_tree.Nodes.TryGetValue(key, out var node)) return;
        node.Choices.Clear();
        foreach (System.Data.DataRow row in dt.Rows)
        {
            string text = row["text"]?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(text)) continue;
            node.Choices.Add(new DialogueChoice
            {
                Text = text,
                NextNodeId = string.IsNullOrWhiteSpace(row["next"]?.ToString()) ? null : row["next"].ToString(),
                Action = string.IsNullOrWhiteSpace(row["action"]?.ToString()) ? null : row["action"].ToString(),
                Condition = string.IsNullOrWhiteSpace(row["condition"]?.ToString()) ? null : row["condition"].ToString()
            });
        }
    }

    private void AddNode()
    {
        if (_tree == null) return;
        string baseKey = "node";
        string key = baseKey;
        int i = 1;
        while (_tree.Nodes.ContainsKey(key)) key = baseKey + i++;
        _tree.Nodes[key] = new DialogueNode { Speaker = _npcName(), Text = "Новый узел" };
        RefreshTree();
        var node = _nodeTree.Nodes.OfType<TreeNode>().FirstOrDefault(n => n.Tag?.ToString() == key);
        if (node != null) _nodeTree.SelectedNode = node;
        LoadNodeEditor();
    }

    private void DeleteNode()
    {
        if (_tree == null) return;
        string? key = SelectedNodeKey();
        if (key == null) return;
        _tree.Nodes.Remove(key);
        RefreshTree();
        LoadNodeEditor();
    }

    private void RenameNode()
    {
        if (_nodeTree.SelectedNode == null) return;
        _nodeTree.SelectedNode.BeginEdit();
    }

    private void RenameNodeTo(string newKey)
    {
        if (_tree == null) return;
        string? oldKey = _nodeTree.SelectedNode?.Tag?.ToString();
        if (oldKey == null || oldKey == newKey) return;
        if (string.IsNullOrWhiteSpace(newKey) || _tree.Nodes.ContainsKey(newKey))
        {
            RefreshTree();
            return;
        }
        var node = _tree.Nodes[oldKey];
        _tree.Nodes.Remove(oldKey);
        _tree.Nodes[newKey] = node;
        // Обновляем ссылки next на старый ключ.
        foreach (var n in _tree.Nodes.Values)
            foreach (var c in n.Choices)
                if (c.NextNodeId == oldKey) c.NextNodeId = newKey;
        RefreshTree();
        var tn = _nodeTree.Nodes.OfType<TreeNode>().FirstOrDefault(n => n.Tag?.ToString() == newKey);
        if (tn != null) _nodeTree.SelectedNode = tn;
        _nodeKeyBox.Text = newKey;
    }

    private void SaveDialogue()
    {
        try
        {
            ApplyNodeEdit();
            ApplyChoiceEdit();
            if (_tree == null || string.IsNullOrEmpty(_currentNpcId))
            {
                _status.Text = "Не выбран NPC";
                return;
            }
            if (_tree.Nodes.Count == 0)
            {
                _status.Text = "Диалог пуст — сохранение отменено";
                return;
            }
            string json = JsonSerializer.Serialize(_tree.Nodes, new JsonSerializerOptions { WriteIndented = true });

            using var conn = new SqliteConnection($"Data Source={_dbFile}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE npcs SET data = $d WHERE id = $id";
            cmd.Parameters.AddWithValue("$d", json);
            cmd.Parameters.AddWithValue("$id", _currentNpcId);
            int affected = cmd.ExecuteNonQuery();
            _status.Text = affected > 0
                ? $"Диалог '{_currentNpcId}' сохранён ({_tree.Nodes.Count} узлов)"
                : $"NPC '{_currentNpcId}' не найден";
        }
        catch (Exception ex) { _status.Text = "Ошибка: " + ex.Message; }
    }

    // === helpers ===

    private Panel MakeField(string labelText, Control ctrl, int height = 26)
    {
        var p = new Panel { Dock = DockStyle.Top, Height = height };
        var lbl = new Label { Text = labelText, Dock = DockStyle.Left, Width = 110, TextAlign = ContentAlignment.MiddleRight };
        ctrl.Dock = DockStyle.Fill;
        p.Controls.Add(ctrl);
        p.Controls.Add(lbl);
        return p;
    }

    private Button MakeButton(string text)
    {
        return new Button
        {
            Text = text,
            Dock = DockStyle.Left,
            Width = 104,
            Height = 28,
            FlatStyle = FlatStyle.Standard,
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 0, 4, 0)};
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
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            BorderStyle = BorderStyle.FixedSingle};
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}

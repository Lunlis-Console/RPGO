using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using LostAndDivine.Shared.Models;
using Microsoft.Data.Sqlite;

namespace LostAndDivine.Editor.Views;

/// <summary>
/// Редактор диалогов NPC (WPF-версия). Исправлены баги WinForms-версии:
/// — при переключении NPC узел НЕ выбирается автоматически (нечего «подхватывать»);
/// — открывается по выбранному в таблице NPC (передаётся id);
/// — отслеживание изменений с подтверждением при переключении/закрытии;
/// — переименование узла — через диалог, без LabelEdit.
/// Сохраняет в столбец data таблицы npcs (JSON в формате DialogueParser).
/// </summary>
public sealed class DialogueEditorWindow : Window
{
    private readonly Db _db;

    private ComboBox _npcCombo = null!;
    private ListBox _nodeList = null!;
    private TextBox _speakerBox = null!;
    private TextBox _textBox = null!;
    private DataGrid _choicesGrid = null!;
    private TextBlock _status = null!;
    private Button _saveBtn = null!;
    private TextBlock _dirtyLabel = null!;
    private Border _nodeEditorPanel = null!;
    private TextBlock _noNodeHint = null!;
    private Button _delNodeBtn = null!;
    private Button _renameBtn = null!;

    private readonly List<(string Id, string Name, string Type)> _npcs = new();
    private DialogueTree? _tree;
    private string _currentNpcId = "";
    private bool _dirty;
    private bool _loading;
    private int _prevNpcIndex = -1;
    private string? _selectedKey => _nodeList.SelectedItem as string;

    public DialogueEditorWindow(Db db, string? presetNpcId = null)
    {
        _db = db;
        Title = "Редактор диалогов NPC";
        Width = 1000;
        Height = 680;
        MinWidth = 800;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        BuildUi();
        LoadNpcs(presetNpcId);
    }

    private void BuildUi()
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // ── верх: выбор NPC ──
        var top = new DockPanel { Margin = new Thickness(8, 6, 8, 4) };
        var npcLabel = new TextBlock { Text = "NPC:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        DockPanel.SetDock(npcLabel, Dock.Left);
        _npcCombo = new ComboBox { Width = 340, HorizontalAlignment = HorizontalAlignment.Left };
        _npcCombo.SelectionChanged += OnNpcComboChanged;
        _dirtyLabel = new TextBlock { Text = "", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), Foreground = System.Windows.Media.Brushes.OrangeRed };
        DockPanel.SetDock(_npcCombo, Dock.Left);
        top.Children.Add(npcLabel);
        top.Children.Add(_npcCombo);
        top.Children.Add(_dirtyLabel);
        Grid.SetRow(top, 0);

        // ── центр: слева узлы, справа редактор ──
        var center = new Grid { Margin = new Thickness(8, 0, 8, 4) };
        center.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });
        center.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var left = new DockPanel();
        var nodeButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        DockPanel.SetDock(nodeButtons, Dock.Top);
        var addNodeBtn = new Button { Content = "Добавить узел", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 6, 0) };
        addNodeBtn.Click += (s, e) => AddNode();
        _renameBtn = new Button { Content = "Переименовать", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 6, 0) };
        _renameBtn.Click += (s, e) => RenameNode();
        _delNodeBtn = new Button { Content = "Удалить", Padding = new Thickness(8, 2, 8, 2) };
        _delNodeBtn.Click += (s, e) => DeleteNode();
        nodeButtons.Children.Add(addNodeBtn);
        nodeButtons.Children.Add(_renameBtn);
        nodeButtons.Children.Add(_delNodeBtn);
        var treeHint = new TextBlock
        {
            Text = "Узлы диалога (ключи). Реплика — справа.",
            Foreground = System.Windows.Media.Brushes.Gray,
            Margin = new Thickness(0, 3, 0, 0)
        };
        DockPanel.SetDock(treeHint, Dock.Bottom);
        _nodeList = new ListBox();
        _nodeList.SelectionChanged += (s, e) => LoadNodeEditor();
        left.Children.Add(treeHint);
        left.Children.Add(_nodeList);
        left.Children.Add(nodeButtons);
        Grid.SetColumn(left, 0);

        var right = new Grid();
        right.ColumnDefinitions.Add(new ColumnDefinition());
        right.RowDefinitions.Add(new RowDefinition());
        right.RowDefinitions.Add(new RowDefinition());
        right.RowDefinitions.Add(new RowDefinition());
        right.RowDefinitions.Add(new RowDefinition());

        _noNodeHint = new TextBlock
        {
            Text = "Выберите узел слева, чтобы редактировать его реплику и варианты ответов.",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = System.Windows.Media.Brushes.Gray,
            FontSize = 14
        };
        Grid.SetRowSpan(_noNodeHint, 4);
        right.Children.Add(_noNodeHint);

        _nodeEditorPanel = new Border { Padding = new Thickness(4) };

        var editorGrid = new Grid();
        editorGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        editorGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        editorGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        editorGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        editorGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var speakerLbl = new TextBlock { Text = "Кто говорит:", Margin = new Thickness(0, 0, 0, 2) };
        _speakerBox = new TextBox { Margin = new Thickness(0, 0, 0, 6) };
        _speakerBox.TextChanged += (s, e) => ApplyNodeEdit();
        Grid.SetRow(speakerLbl, 0);
        Grid.SetRow(_speakerBox, 1);

        var textLbl = new TextBlock { Text = "Реплика:", Margin = new Thickness(0, 0, 0, 2) };
        _textBox = new TextBox { Height = 110, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 0, 0, 6) };
        _textBox.TextChanged += (s, e) => ApplyNodeEdit();
        Grid.SetRow(textLbl, 2);
        Grid.SetRow(_textBox, 3);

        var choicesPanel = new DockPanel();
        var choicesLbl = new TextBlock { Text = "Варианты ответа (text = ответ, next = следующий узел, action = действие, condition = условие)", Margin = new Thickness(0, 4, 0, 2) };
        DockPanel.SetDock(choicesLbl, Dock.Top);
        var choiceButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 3) };
        DockPanel.SetDock(choiceButtons, Dock.Top);
        var addChoiceBtn = new Button { Content = "Добавить вариант", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 6, 0) };
        addChoiceBtn.Click += (s, e) => AddChoice();
        var delChoiceBtn = new Button { Content = "Удалить вариант", Padding = new Thickness(8, 2, 8, 2) };
        delChoiceBtn.Click += (s, e) => DeleteChoice();
        choiceButtons.Children.Add(addChoiceBtn);
        choiceButtons.Children.Add(delChoiceBtn);
        _choicesGrid = new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            RowHeaderWidth = 0,
            SelectionMode = DataGridSelectionMode.Single,
            SelectionUnit = DataGridSelectionUnit.FullRow
        };
        _choicesGrid.Columns.Add(new DataGridTextColumn { Header = "Текст ответа", Binding = new Binding("Text") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = new DataGridLength(3, DataGridLengthUnitType.Star) });
        _choicesGrid.Columns.Add(new DataGridTextColumn { Header = "Следующий узел", Binding = new Binding("Next") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = new DataGridLength(1.4, DataGridLengthUnitType.Star) });
        _choicesGrid.Columns.Add(new DataGridTextColumn { Header = "Действие", Binding = new Binding("Action") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = new DataGridLength(1.4, DataGridLengthUnitType.Star) });
        _choicesGrid.Columns.Add(new DataGridTextColumn { Header = "Условие", Binding = new Binding("Condition") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = new DataGridLength(1.4, DataGridLengthUnitType.Star) });
        _choicesGrid.CellEditEnding += (s, e) => { if (e.EditAction == DataGridEditAction.Commit) ApplyChoices(); };
        choicesPanel.Children.Add(_choicesGrid);
        choicesPanel.Children.Add(choiceButtons);
        choicesPanel.Children.Add(choicesLbl);
        Grid.SetRow(choicesPanel, 4);

        editorGrid.Children.Add(speakerLbl);
        editorGrid.Children.Add(_speakerBox);
        editorGrid.Children.Add(textLbl);
        editorGrid.Children.Add(_textBox);
        editorGrid.Children.Add(choicesPanel);
        _nodeEditorPanel.Child = editorGrid;
        Grid.SetColumn(_nodeEditorPanel, 1);
        right.Children.Add(_nodeEditorPanel);

        center.Children.Add(left);
        center.Children.Add(right);
        Grid.SetRow(center, 1);

        // ── низ: статус + кнопки ──
        var bottom = new DockPanel { Margin = new Thickness(8, 0, 8, 8) };
        _status = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Foreground = System.Windows.Media.Brushes.Gray };
        DockPanel.SetDock(_status, Dock.Left);
        _saveBtn = new Button { Content = "Сохранить диалог", Width = 160, Height = 30, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Right };
        _saveBtn.Click += (s, e) => SaveDialogue();
        var closeBtn = new Button { Content = "Закрыть", Width = 100, Height = 30, Margin = new Thickness(8, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
        closeBtn.Click += (s, e) => Close();
        bottom.Children.Add(_saveBtn);
        bottom.Children.Add(closeBtn);
        bottom.Children.Add(_status);
        Grid.SetRow(bottom, 2);

        grid.Children.Add(top);
        grid.Children.Add(center);
        grid.Children.Add(bottom);
        Content = grid;

        UpdateEditorEnabled(false);
        UpdateDirty();
    }

    private void LoadNpcs(string? presetNpcId)
    {
        _npcs.Clear();
        using var conn = _db.OpenContent();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, type FROM npcs ORDER BY id";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            _npcs.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        _npcCombo.ItemsSource = _npcs.Select(n => $"{n.Id} — {n.Name} [{n.Type}]").ToList();
        if (!string.IsNullOrEmpty(presetNpcId))
        {
            int idx = _npcs.FindIndex(n => string.Equals(n.Id, presetNpcId, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                _npcCombo.SelectedIndex = idx;
            }
            else _status.Text = "NPC не найден в базе";
        }
        else if (_npcs.Count > 0)
        {
            _status.Text = "Выберите NPC из списка";
        }
        else
        {
            _status.Text = "NPC не найдены в базе";
        }
    }

    private void OnNpcComboChanged(object sender, SelectionChangedEventArgs e) => SwitchNpc();

    private void SwitchNpc()
    {
        if (_loading) return;
        if (!string.IsNullOrEmpty(_currentNpcId) && _dirty && !ConfirmDiscard())
        {
            // возвращаем предыдущий выбор
            _npcCombo.SelectionChanged -= OnNpcComboChanged;
            _npcCombo.SelectedIndex = _prevNpcIndex;
            _npcCombo.SelectionChanged += OnNpcComboChanged;
            return;
        }
        _dirty = false;
        _prevNpcIndex = _npcCombo.SelectedIndex;
        LoadDialogueForSelected();
    }

    private bool ConfirmDiscard()
    {
        var res = MessageBox.Show(this, "Есть несохранённые изменения. Отменить их и переключиться?",
            "Изменения не сохранены", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        return res == MessageBoxResult.Yes;
    }

    private void LoadDialogueForSelected()
    {
        _currentNpcId = _npcCombo.SelectedIndex >= 0 && _npcCombo.SelectedIndex < _npcs.Count
            ? _npcs[_npcCombo.SelectedIndex].Id
            : "";
        _tree = null;
        _nodeList.ItemsSource = null;
        UpdateEditorEnabled(false);

        if (string.IsNullOrEmpty(_currentNpcId)) return;
        string? data = null;
        using (var conn = _db.OpenContent())
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT data FROM npcs WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", _currentNpcId);
            var v = cmd.ExecuteScalar();
            data = v?.ToString();
        }
        _tree = DialogueParser.Parse(data) ?? new DialogueTree();
        _nodeList.ItemsSource = _tree.Nodes.Keys.ToList();
        _status.Text = $"Узлов: {_tree.Nodes.Count} — редактирование '{_currentNpcId}'";
        _noNodeHint.Text = _tree.Nodes.Count == 0
            ? "У этого NPC нет узлов — нажмите «Добавить узел»."
            : "Выберите узел слева, чтобы редактировать его реплику и варианты ответов.";
        UpdateEditorEnabled(false);
    }

    private void UpdateEditorEnabled(bool enabled)
    {
        bool hasSelection = enabled && _selectedKey != null;
        _speakerBox.IsEnabled = hasSelection;
        _textBox.IsEnabled = hasSelection;
        _choicesGrid.IsEnabled = hasSelection;
        _delNodeBtn.IsEnabled = hasSelection;
        _renameBtn.IsEnabled = hasSelection;
        _noNodeHint.Visibility = hasSelection ? Visibility.Collapsed : Visibility.Visible;
        _nodeEditorPanel.Visibility = hasSelection ? Visibility.Visible : Visibility.Collapsed;
        if (enabled && _selectedKey == null)
            _noNodeHint.Text = "Выберите узел слева, чтобы редактировать его реплику и варианты ответов.";
    }

    private void LoadNodeEditor()
    {
        if (_tree == null) return;
        string? key = _selectedKey;
        if (key == null || !_tree.Nodes.TryGetValue(key, out var node))
        {
            _loading = true;
            _speakerBox.Text = "";
            _textBox.Text = "";
            _choicesGrid.ItemsSource = null;
            _loading = false;
            UpdateEditorEnabled(true);
            return;
        }
        _loading = true;
        _speakerBox.Text = node.Speaker;
        _textBox.Text = node.Text;
        var rows = node.Choices.Select(c => new ChoiceRow
        {
            Text = c.Text,
            Next = c.NextNodeId ?? "",
            Action = c.Action ?? "",
            Condition = c.Condition ?? ""
        });
        _choicesGrid.ItemsSource = new ObservableCollection<ChoiceRow>(rows);
        _loading = false;
        UpdateEditorEnabled(true);
    }

    private void ApplyNodeEdit()
    {
        if (_loading || _tree == null) return;
        string? key = _selectedKey;
        if (key == null || !_tree.Nodes.TryGetValue(key, out var node)) return;
        node.Speaker = _speakerBox.Text;
        node.Text = _textBox.Text;
        MarkDirty();
    }

    private void ApplyChoices()
    {
        if (_loading || _tree == null) return;
        string? key = _selectedKey;
        if (key == null || !_tree.Nodes.TryGetValue(key, out var node)) return;
        node.Choices.Clear();
        if (_choicesGrid.ItemsSource is IEnumerable<ChoiceRow> rows)
        {
            foreach (var r in rows)
            {
                if (string.IsNullOrWhiteSpace(r.Text)) continue;
                node.Choices.Add(new DialogueChoice
                {
                    Text = r.Text,
                    NextNodeId = string.IsNullOrWhiteSpace(r.Next) ? null : r.Next.Trim(),
                    Action = string.IsNullOrWhiteSpace(r.Action) ? null : r.Action.Trim(),
                    Condition = string.IsNullOrWhiteSpace(r.Condition) ? null : r.Condition.Trim()
                });
            }
        }
        MarkDirty();
    }

    private void AddChoice()
    {
        if (_selectedKey == null || _tree == null) return;
        var rows = _choicesGrid.ItemsSource as ObservableCollection<ChoiceRow>
                   ?? new ObservableCollection<ChoiceRow>();
        if (_choicesGrid.ItemsSource == null) _choicesGrid.ItemsSource = rows;
        rows.Add(new ChoiceRow { Text = "" });
        _choicesGrid.SelectedItem = rows[^1];
        ApplyChoices();
    }

    private void DeleteChoice()
    {
        if (_choicesGrid.SelectedItem is not ChoiceRow row) return;
        if (_choicesGrid.ItemsSource is ObservableCollection<ChoiceRow> rows)
        {
            rows.Remove(row);
            ApplyChoices();
        }
    }

    private void AddNode()
    {
        if (_tree == null || string.IsNullOrEmpty(_currentNpcId)) return;
        string baseKey = "node";
        string key = baseKey;
        int i = 1;
        while (_tree.Nodes.ContainsKey(key)) key = baseKey + i++;
        _tree.Nodes[key] = new DialogueNode { Speaker = CurrentNpcName(), Text = "Новый узел" };
        _nodeList.ItemsSource = _tree.Nodes.Keys.ToList();
        _nodeList.SelectedItem = key;
        MarkDirty();
        _status.Text = $"Узлов: {_tree.Nodes.Count} — редактирование '{_currentNpcId}'";
    }

    private void DeleteNode()
    {
        if (_tree == null) return;
        string? key = _selectedKey;
        if (key == null) return;
        if (MessageBox.Show(this, $"Удалить узел «{key}»?", "Удаление узла",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _tree.Nodes.Remove(key);
        _nodeList.ItemsSource = _tree.Nodes.Keys.ToList();
        MarkDirty();
        _status.Text = $"Узлов: {_tree.Nodes.Count} — редактирование '{_currentNpcId}'";
    }

    private void RenameNode()
    {
        if (_tree == null) return;
        string? oldKey = _selectedKey;
        if (oldKey == null) return;
        var dlg = new RenameDialog(oldKey);
        dlg.Owner = this;
        if (dlg.ShowDialog() != true) return;
        string newKey = dlg.NewKey.Trim();
        if (string.IsNullOrWhiteSpace(newKey) || newKey == oldKey) return;
        if (_tree.Nodes.ContainsKey(newKey))
        {
            MessageBox.Show(this, $"Узел «{newKey}» уже существует.", "Переименование",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var node = _tree.Nodes[oldKey];
        _tree.Nodes.Remove(oldKey);
        _tree.Nodes[newKey] = node;
        foreach (var n in _tree.Nodes.Values)
            foreach (var c in n.Choices)
                if (c.NextNodeId == oldKey) c.NextNodeId = newKey;
        _nodeList.ItemsSource = _tree.Nodes.Keys.ToList();
        _nodeList.SelectedItem = newKey;
        MarkDirty();
    }

    private string CurrentNpcName()
    {
        if (_npcCombo.SelectedIndex >= 0 && _npcCombo.SelectedIndex < _npcs.Count)
            return _npcs[_npcCombo.SelectedIndex].Name;
        return "";
    }

    private void MarkDirty()
    {
        if (_loading) return;
        _dirty = true;
        UpdateDirty();
    }

    private void UpdateDirty()
    {
        _dirtyLabel.Text = _dirty ? "● не сохранено" : "";
    }

    private void SaveDialogue()
    {
        try
        {
            ApplyNodeEdit();
            ApplyChoices();
            if (_tree == null || string.IsNullOrEmpty(_currentNpcId))
            {
                _status.Text = "Не выбран NPC";
                return;
            }
            if (_tree.Nodes.Count == 0)
            {
                MessageBox.Show(this, "Диалог пуст — сохранение отменено.\nДобавьте хотя бы один узел.",
                    "Сохранение", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            string json = JsonSerializer.Serialize(_tree.Nodes, new JsonSerializerOptions { WriteIndented = true });
            using var conn = _db.OpenContent();
            using var tx = conn.BeginTransaction();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE npcs SET data = $d WHERE id = $id";
            cmd.Parameters.AddWithValue("$d", json);
            cmd.Parameters.AddWithValue("$id", _currentNpcId);
            int affected = cmd.ExecuteNonQuery();
            if (affected > 0)
            {
                tx.Commit();
                _dirty = false;
                UpdateDirty();
                _status.Text = $"Диалог '{_currentNpcId}' сохранён ({_tree.Nodes.Count} узлов)";
            }
            else
            {
                tx.Rollback();
                _status.Text = $"NPC '{_currentNpcId}' не найден";
            }
        }
        catch (Exception ex) { _status.Text = "Ошибка: " + ex.Message; }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_dirty)
        {
            var res = MessageBox.Show(this, "Есть несохранённые изменения. Закрыть без сохранения?",
                "Изменения не сохранены", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (res != MessageBoxResult.Yes) e.Cancel = true;
        }
    }

    private sealed class ChoiceRow
    {
        public string Text { get; set; } = "";
        public string Next { get; set; } = "";
        public string Action { get; set; } = "";
        public string Condition { get; set; } = "";
    }

    private sealed class RenameDialog : Window
    {
        public string NewKey { get; private set; } = "";

        public RenameDialog(string currentKey)
        {
            Title = "Переименовать узел";
            Width = 360;
            Height = 140;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            var panel = new StackPanel { Margin = new Thickness(12) };
            panel.Children.Add(new TextBlock { Text = "Новый ключ узла:", Margin = new Thickness(0, 0, 0, 4) });
            var box = new TextBox { Text = currentKey };
            panel.Children.Add(box);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var ok = new Button { Content = "OK", Width = 80, Margin = new Thickness(0, 0, 8, 0) };
            ok.Click += (s, e) => { NewKey = box.Text; DialogResult = true; };
            var cancel = new Button { Content = "Отмена", Width = 80 };
            cancel.Click += (s, e) => DialogResult = false;
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            panel.Children.Add(buttons);
            Content = panel;
            Loaded += (s, e) => { box.Focus(); box.SelectAll(); };
        }
    }
}
using System.Data;
using System.Windows;
using System.Windows.Controls;

namespace LostAndDivine.Editor.Views;

/// <summary>Полный редактор квеста. Порт QuestEditForm (WinForms).</summary>
public sealed class QuestEditorWindow : Window
{
    private readonly Db _db;
    private readonly DataRow _row;

    private TextBox _idBox = null!, _titleBox = null!, _descBox = null!, _locBox = null!, _zoneBox = null!, _chainBox = null!;
    private ComboBox _giverBox = null!, _monsterBox = null!, _itemBox = null!, _useItemBox = null!, _npcBox = null!, _prereqBox = null!, _itemRewardBox = null!;
    private ComboBox _typeCombo = null!;
    private TextBox _xBox = null!, _yBox = null!, _targetBox = null!, _minLvlBox = null!, _stepBox = null!, _xpBox = null!, _goldBox = null!, _itemRewardCountBox = null!;
    private CheckBox _storyBox = null!, _autoBox = null!, _repBox = null!;
    private string _lastGiverLoc = "";

    public QuestEditorWindow(Db db, DataRow row)
    {
        _db = db;
        _row = row;
        Title = "Квест: " + Cell("title") + " [" + Cell("id") + "]";
        Width = 600;
        Height = 840;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var stack = new StackPanel { Margin = new Thickness(12) };

        var grpMain = Group("Основное");
        _idBox = AddText(grpMain, "ID:", Cell("id"));
        _titleBox = AddText(grpMain, "Название:", Cell("title"));
        _typeCombo = AddCombo(grpMain, "Тип:", Ui.QuestTypes, Cell("type"));
        _giverBox = AddCombo(grpMain, "NPC (выдаёт):", db.NpcRefs.Select(r => r.Name).ToList(), Cell("giver_npc"));
        _storyBox = AddCheck(grpMain, "Сюжетный:", Db.IsChecked(Cell("is_story")));
        _repBox = AddCheck(grpMain, "Повторяемый:", Db.IsChecked(Cell("repeatable")));
        _locBox = AddText(grpMain, "Локация:", Cell("location"));
        if (string.IsNullOrWhiteSpace(_locBox.Text))
            _locBox.Text = db.LocationByName(_giverBox.Text);

        var grpDesc = Group("Описание");
        _descBox = new TextBox { Text = Cell("description"), Height = 70, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        grpDesc.Children.Add(_descBox);

        var grpGoal = Group("Цель");
        _monsterBox = AddCombo(grpGoal, "Монстр (kill):", db.MonsterRefs.Select(r => r.Name).ToList(), Cell("monster"));
        _itemBox = AddCombo(grpGoal, "Предмет (collect):", db.CollectibleRefs.Select(r => r.Name).ToList(), Cell("item"));
        _useItemBox = AddCombo(grpGoal, "Предмет (use):", db.RewardItemRefs.Select(r => r.Name).ToList(), Cell("use_item"));
        _npcBox = AddCombo(grpGoal, "NPC (talk/travel):", db.NpcRefs.Select(r => r.Name).ToList(), Cell("npc"));
        _zoneBox = AddText(grpGoal, "Зона (explore/авто):", Cell("target_zone"));
        _xBox = AddNum(grpGoal, "Точка X (travel):", Cell("target_x"));
        _yBox = AddNum(grpGoal, "Точка Y (travel):", Cell("target_y"));
        _targetBox = AddNum(grpGoal, "Кол-во:", Cell("target"));

        var grpCond = Group("Условия и выдача");
        _autoBox = AddCheck(grpCond, "Авто-выдача при входе в зону:", Db.IsChecked(Cell("auto_grant")));
        _minLvlBox = AddNum(grpCond, "Мин. уровень:", Cell("min_level"));
        _chainBox = AddText(grpCond, "Цепочка (ID):", Cell("chain_id"));
        _stepBox = AddNum(grpCond, "Шаг в цепочке:", Cell("step"));
        var prereqNames = new List<string> { "" };
        prereqNames.AddRange(db.QuestRefs.Select(r => r.Name));
        _prereqBox = AddCombo(grpCond, "Предусловие (квест):", prereqNames, Cell("prereq"));

        var grpReward = Group("Награды");
        _xpBox = AddNum(grpReward, "Опыт:", Cell("xp_reward"));
        _goldBox = AddNum(grpReward, "Золото:", Cell("gold_reward"));
        _itemRewardBox = AddCombo(grpReward, "Награда (предмет):", db.RewardItemRefs.Select(r => r.Name).ToList(), Cell("item_reward"));
        _itemRewardCountBox = AddNum(grpReward, "Награда (кол-во):", Cell("item_reward_count"));

        var grpDlg = Group("Диалоги (NPC-выдатчик)");
        var dlgBtn = new Button { Content = "Открыть диалог NPC-выдатчика...", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 3, 10, 3) };
        dlgBtn.Click += (s, e) => OpenGiverDialogue();
        grpDlg.Children.Add(dlgBtn);

        stack.Children.Add(grpMain);
        stack.Children.Add(grpDesc);
        stack.Children.Add(grpGoal);
        stack.Children.Add(grpCond);
        stack.Children.Add(grpReward);
        stack.Children.Add(grpDlg);
        scroll.Content = stack;

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 12, 8) };
        var okBtn = new Button { Content = "Сохранить", Width = 110, Padding = new Thickness(0, 3, 0, 3), FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 8, 0) };
        okBtn.Click += (s, e) => Save();
        var cancelBtn = new Button { Content = "Отмена", Width = 100, Padding = new Thickness(0, 3, 0, 3) };
        cancelBtn.Click += (s, e) => Close();
        buttons.Children.Add(okBtn);
        buttons.Children.Add(cancelBtn);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(scroll, 0);
        Grid.SetRow(buttons, 1);
        root.Children.Add(scroll);
        root.Children.Add(buttons);
        Content = root;

        _typeCombo.SelectionChanged += (s, e) => UpdateFields();
        _storyBox.Checked += (s, e) =>
        {
            _repBox.IsEnabled = _storyBox.IsChecked != true;
            if (_storyBox.IsChecked == true) _repBox.IsChecked = false;
        };
        _repBox.IsEnabled = _storyBox.IsChecked != true;
        _giverBox.SelectionChanged += (s, e) =>
        {
            if (string.IsNullOrWhiteSpace(_locBox.Text) || _locBox.Text == _lastGiverLoc)
                _locBox.Text = _db.LocationByName(_giverBox.Text);
        };
        _lastGiverLoc = _db.LocationByName(_giverBox.Text);
        UpdateFields();
    }

    private void OpenGiverDialogue()
    {
        string npcName = _giverBox.Text;
        var npc = _db.NpcRefs.FirstOrDefault(r => r.Name == npcName);
        if (string.IsNullOrEmpty(npc.Id))
        {
            MessageBox.Show(this, "Сначала выберите NPC-выдатчика.", "Диалоги", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dlg = new DialogueEditorWindow(_db, npc.Id);
        dlg.Owner = this;
        dlg.ShowDialog();
    }

    private void UpdateFields()
    {
        string t = _typeCombo.SelectedItem?.ToString() ?? "";
        _monsterBox.IsEnabled = t == "kill";
        _itemBox.IsEnabled = t == "collect";
        _useItemBox.IsEnabled = t == "use";
        _npcBox.IsEnabled = t is "talk" or "travel";
        _zoneBox.IsEnabled = t is "explore" or "travel";
        _xBox.IsEnabled = t == "travel";
        _yBox.IsEnabled = t == "travel";
        _targetBox.IsEnabled = t is "kill" or "collect" or "use" or "talk";
    }

    private void Save()
    {
        _row["id"] = _idBox.Text.Trim();
        _row["title"] = _titleBox.Text.Trim();
        _row["type"] = _typeCombo.SelectedItem?.ToString() ?? "kill";
        _row["giver_npc"] = _giverBox.Text;
        _row["is_story"] = _storyBox.IsChecked == true;
        _row["repeatable"] = _repBox.IsChecked == true;
        _row["location"] = _locBox.Text.Trim();
        _row["description"] = _descBox.Text;
        _row["monster"] = _monsterBox.Text;
        _row["item"] = _itemBox.Text;
        _row["use_item"] = _useItemBox.Text;
        _row["npc"] = _npcBox.Text;
        _row["target_zone"] = _zoneBox.Text.Trim();
        _row["target_x"] = Num(_xBox);
        _row["target_y"] = Num(_yBox);
        _row["target"] = Num(_targetBox);
        _row["auto_grant"] = _autoBox.IsChecked == true;
        _row["min_level"] = Num(_minLvlBox);
        _row["chain_id"] = _chainBox.Text.Trim();
        _row["step"] = Num(_stepBox);
        _row["prereq"] = _prereqBox.Text;
        _row["xp_reward"] = Num(_xpBox);
        _row["gold_reward"] = Num(_goldBox);
        _row["item_reward"] = _itemRewardBox.Text;
        _row["item_reward_count"] = Num(_itemRewardCountBox);
        Close();
    }

    private string Cell(string col) => _row[col]?.ToString() ?? "";
    private static int Num(TextBox tb) => int.TryParse(tb.Text, out int v) ? v : 0;

    private static StackPanel Group(string title)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 4, 0, 8) };
        sp.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4) });
        return sp;
    }

    private TextBox AddText(StackPanel g, string label, string value)
    {
        var tb = new TextBox { Text = value };
        AddRow(g, label, tb);
        return tb;
    }

    private TextBox AddNum(StackPanel g, string label, string value)
    {
        var tb = new TextBox { Text = value, Width = 120, HorizontalAlignment = HorizontalAlignment.Left };
        AddRow(g, label, tb);
        return tb;
    }

    private ComboBox AddCombo(StackPanel g, string label, IEnumerable<string> items, string current)
    {
        var list = items.ToList();
        var cb = new ComboBox { ItemsSource = list, Width = 260, HorizontalAlignment = HorizontalAlignment.Left };
        if (list.Contains(current)) cb.SelectedItem = current;
        else if (list.Count > 0) cb.SelectedIndex = 0;
        AddRow(g, label, cb);
        return cb;
    }

    private CheckBox AddCheck(StackPanel g, string label, bool isChecked)
    {
        var cb = new CheckBox { IsChecked = isChecked, VerticalAlignment = VerticalAlignment.Center };
        AddRow(g, label, cb);
        return cb;
    }

    private static void AddRow(StackPanel g, string label, Control control)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var lbl = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        Grid.SetColumn(lbl, 0);
        Grid.SetColumn(control, 1);
        grid.Children.Add(lbl);
        grid.Children.Add(control);
        g.Children.Add(grid);
    }
}
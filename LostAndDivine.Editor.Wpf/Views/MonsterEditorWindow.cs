using System.Data;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace LostAndDivine.Editor.Views;

/// <summary>Полный редактор монстра с дропом. Порт MonsterEditForm (WinForms).</summary>
public sealed class MonsterEditorWindow : Window
{
    private readonly DataRow _row;

    private TextBox _idBox = null!, _nameBox = null!, _symbolBox = null!;
    private TextBox _tierBox = null!, _hpBox = null!, _paBox = null!, _pdBox = null!, _xpBox = null!, _goldBox = null!, _goldMaxBox = null!;
    private TextBox _strBox = null!, _endBox = null!, _agiBox = null!, _cunBox = null!, _intBox = null!, _wisBox = null!;
    private TextBox _ccBox = null!, _cdBox = null!, _ecBox = null!, _bcBox = null!, _pcBox = null!, _sdBox = null!;
    private DataGrid _dropsGrid = null!;
    private List<string> _itemNames = new();

    public MonsterEditorWindow(Db db, DataRow row)
    {
        _row = row;
        Title = "Монстр: " + Cell("name") + " [" + Cell("id") + "]";
        Width = 600;
        Height = 820;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var stack = new StackPanel { Margin = new Thickness(12) };

        var grpMain = Group("Основное");
        _idBox = AddText(grpMain, "ID:", Cell("id"));
        _nameBox = AddText(grpMain, "Имя:", Cell("name"));
        _tierBox = AddNum(grpMain, "Уровень:", Cell("tier"));
        _symbolBox = AddText(grpMain, "Символ (на карте):", Cell("symbol"));

        var grpStats = Group("Характеристики");
        _hpBox = AddNum(grpStats, "HP:", Cell("health"));
        _paBox = AddNum(grpStats, "Физ. атака:", Cell("phys_attack"));
        _pdBox = AddNum(grpStats, "Физ. защита:", Cell("phys_defense"));
        _xpBox = AddNum(grpStats, "Опыт за убийство:", Cell("xp_reward"));
        _goldBox = AddNum(grpStats, "Золото мин:", Cell("gold_reward"));
        _goldMaxBox = AddNum(grpStats, "Золото макс (0 = без разброса):", Cell("gold_max"));

        var grpAttr = Group("Атрибуты");
        _strBox = AddNum(grpAttr, "Сила:", Cell("strength"));
        _endBox = AddNum(grpAttr, "Выносливость:", Cell("endurance"));
        _agiBox = AddNum(grpAttr, "Ловкость:", Cell("agility"));
        _cunBox = AddNum(grpAttr, "Хитрость:", Cell("cunning"));
        _intBox = AddNum(grpAttr, "Интеллект:", Cell("intellect"));
        _wisBox = AddNum(grpAttr, "Мудрость:", Cell("wisdom"));

        var grpCombat = Group("Бой");
        _ccBox = AddNum(grpCombat, "Крит. шанс (%):", Cell("crit_chance"));
        _cdBox = AddNum(grpCombat, "Крит. урон (%):", Cell("crit_damage"));
        _ecBox = AddNum(grpCombat, "Уклонение (%):", Cell("evade_chance"));
        _bcBox = AddNum(grpCombat, "Блок (%):", Cell("block_chance"));
        _pcBox = AddNum(grpCombat, "Парирование (%):", Cell("parry_chance"));
        _sdBox = AddNum(grpCombat, "Защита щитом:", Cell("shield_defense"));

        var grpDrops = Group("Дроп (предмет и шанс, %)");
        var hint = new TextBlock
        {
            Text = "Добавьте строку в таблице, выберите предмет и укажите шанс выпадения.",
            Margin = new Thickness(0, 2, 0, 4),
            Foreground = System.Windows.Media.Brushes.Gray
        };
        _dropsGrid = BuildDropsGrid(db);
        LoadDrops(db, Cell("__drops"));
        grpDrops.Children.Add(hint);
        grpDrops.Children.Add(_dropsGrid);

        stack.Children.Add(grpMain);
        stack.Children.Add(grpStats);
        stack.Children.Add(grpAttr);
        stack.Children.Add(grpCombat);
        stack.Children.Add(grpDrops);
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
    }

    private DataGrid BuildDropsGrid(Db db)
    {
        _itemNames = db.LoadRefs("SELECT id, name FROM items ORDER BY id")
            .Select(r => $"{r.Id} — {r.Name}").ToList();
        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserAddRows = true,
            CanUserDeleteRows = true,
            Height = 200,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            RowHeaderWidth = 0,
            SelectionMode = DataGridSelectionMode.Single,
            SelectionUnit = DataGridSelectionUnit.FullRow
        };
        grid.Columns.Add(new DataGridTextColumn { Header = "Предмет", Binding = new System.Windows.Data.Binding("Item") { Mode = System.Windows.Data.BindingMode.TwoWay }, Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Шанс, %", Binding = new System.Windows.Data.Binding("Chance") { Mode = System.Windows.Data.BindingMode.TwoWay }, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        return grid;
    }

    private void LoadDrops(Db db, string json)
    {
        var items = _dropsGrid.ItemsSource as System.Collections.ObjectModel.ObservableCollection<DropRow>
                    ?? new System.Collections.ObjectModel.ObservableCollection<DropRow>();
        if (!string.IsNullOrWhiteSpace(json) && json != "[]")
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    string itemId = el.TryGetProperty("ItemId", out var idProp) ? idProp.GetString() ?? "" : "";
                    string chance = el.TryGetProperty("Chance", out var cProp) ? cProp.ToString() : "0";
                    if (string.IsNullOrWhiteSpace(itemId)) continue;
                    items.Add(new DropRow
                    {
                        Item = itemId,
                        Chance = chance
                    });
                }
            }
            catch { }
        }
        _dropsGrid.ItemsSource = items;
    }

    private sealed class DropRow
    {
        public string Item { get; set; } = "";
        public string Chance { get; set; } = "0";
    }

    private void Save()
    {
        _row["id"] = _idBox.Text.Trim();
        _row["name"] = _nameBox.Text.Trim();
        _row["symbol"] = _symbolBox.Text.Trim();
        _row["tier"] = Num(_tierBox);
        _row["health"] = Num(_hpBox);
        _row["phys_attack"] = Num(_paBox);
        _row["phys_defense"] = Num(_pdBox);
        _row["xp_reward"] = Num(_xpBox);
        _row["gold_reward"] = Num(_goldBox);
        _row["gold_max"] = Num(_goldMaxBox);
        _row["strength"] = Num(_strBox);
        _row["endurance"] = Num(_endBox);
        _row["agility"] = Num(_agiBox);
        _row["cunning"] = Num(_cunBox);
        _row["intellect"] = Num(_intBox);
        _row["wisdom"] = Num(_wisBox);
        _row["crit_chance"] = Dbl(_ccBox);
        _row["crit_damage"] = Dbl(_cdBox);
        _row["evade_chance"] = Dbl(_ecBox);
        _row["block_chance"] = Dbl(_bcBox);
        _row["parry_chance"] = Dbl(_pcBox);
        _row["shield_defense"] = Num(_sdBox);

        _dropsGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var drops = new List<object>();
        if (_dropsGrid.ItemsSource is System.Collections.IEnumerable rows)
        {
            foreach (var r in rows)
            {
                if (r is not DropRow dr) continue;
                string itemId = dr.Item.Contains(" — ") ? dr.Item.Split(" — ")[0].Trim() : dr.Item.Trim();
                if (string.IsNullOrWhiteSpace(itemId)) continue;
                int chance = int.TryParse(dr.Chance, out int c) ? c : 0;
                drops.Add(new { ItemId = itemId, Chance = Math.Clamp(chance, 0, 100) });
            }
        }
        _row["__drops"] = JsonSerializer.Serialize(drops);
        Close();
    }

    private string Cell(string col) => _row[col]?.ToString() ?? "";
    private static int Num(TextBox tb) => int.TryParse(tb.Text, out int v) ? v : 0;
    private static double Dbl(TextBox tb) => double.TryParse(tb.Text, System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : 0;

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
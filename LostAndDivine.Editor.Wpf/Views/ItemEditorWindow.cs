using System.Data;
using System.Windows;
using System.Windows.Controls;

namespace LostAndDivine.Editor.Views;

/// <summary>
/// Полноценный редактор предмета: все поля в группах, качество из описания.
/// Порт ItemEditForm (WinForms).
/// </summary>
public sealed class ItemEditorWindow : Window
{
    private readonly DataRow _row;
    private readonly DataTable _table;

    private TextBox _idBox = null!, _nameBox = null!, _descBox = null!;
    private ComboBox _typeCombo = null!, _qualityCombo = null!, _dmgTypeBox = null!, _subtypeBox = null!;
    private TextBox _reqBox = null!, _valueBox = null!, _stockBox = null!;
    private TextBox _minBox = null!, _maxBox = null!, _defBox = null!, _hpBox = null!, _healBox = null!, _manaBox = null!;
    private CheckBox _thBox = null!;
    private TextBox _strBox = null!, _endBox = null!, _agiBox = null!, _cunBox = null!, _intBox = null!, _wisBox = null!;
    private TextBox _bpaBox = null!, _bmaBox = null!, _bdefBox = null!, _bresBox = null!, _basBox = null!, _ccBox = null!, _cdBox = null!, _ecBox = null!;
    private TextBox _asmBox = null!, _arBox = null!;

    public ItemEditorWindow(Db db, DataRow row, DataTable table)
    {
        _row = row;
        _table = table;
        Title = "Редактирование: " + Cell("name");
        Width = 560;
        Height = 760;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var stack = new StackPanel { Margin = new Thickness(12) };

        var grpMain = Group("Основное");
        _idBox = AddText(grpMain, "ID:", Cell("id"));
        _nameBox = AddText(grpMain, "Название:", Cell("name"));
        _typeCombo = AddCombo(grpMain, "Тип:", Ui.ItemTypes, Cell("type"));
        _qualityCombo = AddCombo(grpMain, "Качество:", new[] { "Обычный", "Необычный", "Редкий", "Эпический" }, QualityFromDesc(Cell("description")));
        _reqBox = AddNum(grpMain, "Треб. уровень:", Cell("required_level"));
        _valueBox = AddNum(grpMain, "Цена:", Cell("value"));
        _stockBox = AddNum(grpMain, "Сток:", Cell("stock"));

        var grpStats = Group("Характеристики");
        _minBox = AddNum(grpStats, "Урон мин:", Cell("damage_min"));
        _maxBox = AddNum(grpStats, "Урон макс:", Cell("damage_max"));
        _defBox = AddNum(grpStats, "Защита:", Cell("defense"));
        _hpBox = AddNum(grpStats, "HP:", Cell("max_health_bonus"));
        _healBox = AddNum(grpStats, "Лечение:", Cell("heal_amount"));
        _manaBox = AddNum(grpStats, "Восст. маны:", Cell("restore_mana"));
        _thBox = AddCheck(grpStats, "Двуручное:", Db.ToInt(Cell("two_handed")) != 0);

        var grpAttr = Group("Бонусы к атрибутам");
        _strBox = AddNum(grpAttr, "Сила:", Cell("bonus_strength"));
        _endBox = AddNum(grpAttr, "Выносливость:", Cell("bonus_endurance"));
        _agiBox = AddNum(grpAttr, "Ловкость:", Cell("bonus_agility"));
        _cunBox = AddNum(grpAttr, "Хитрость:", Cell("bonus_cunning"));
        _intBox = AddNum(grpAttr, "Интеллект:", Cell("bonus_intellect"));
        _wisBox = AddNum(grpAttr, "Мудрость:", Cell("bonus_wisdom"));

        var grpSec = Group("Бонусы к характеристикам");
        _bpaBox = AddNum(grpSec, "+Физ. атака:", Cell("bonus_phys_attack"));
        _bmaBox = AddNum(grpSec, "+Маг. атака:", Cell("bonus_mag_attack"));
        _bdefBox = AddNum(grpSec, "+Защита:", Cell("bonus_defense"));
        _bresBox = AddNum(grpSec, "+Сопротивление:", Cell("bonus_resistance"));
        _basBox = AddNum(grpSec, "+Скор. атаки:", Cell("bonus_attack_speed"));
        _ccBox = AddNum(grpSec, "+Крит. шанс (%):", Cell("bonus_crit_chance"));
        _cdBox = AddNum(grpSec, "+Крит. урон (%):", Cell("bonus_crit_damage"));
        _ecBox = AddNum(grpSec, "+Уклонение (%):", Cell("bonus_evade_chance"));

        var grpWpn = Group("Оружие");
        _dmgTypeBox = AddCombo(grpWpn, "Тип урона:", new[] { "", "slashing", "piercing", "bludgeoning", "magic" }, Cell("damage_type"));
        _subtypeBox = AddCombo(grpWpn, "Подтип:", new[] { "", "sword", "axe", "mace", "dagger", "greatsword", "poleaxe", "hammer", "greathammer", "halberd", "spear", "bow", "staff", "wand", "grimoire", "sphere", "shield" }, Cell("weapon_subtype"));
        _asmBox = AddNum(grpWpn, "Скор. атаки:", Cell("attack_speed_modifier"));
        _arBox = AddNum(grpWpn, "Дальность:", Cell("attack_range"));

        var grpDesc = Group("Описание");
        _descBox = new TextBox { Text = Cell("description"), Height = 70, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        grpDesc.Children.Add(_descBox);

        stack.Children.Add(grpMain);
        stack.Children.Add(grpStats);
        stack.Children.Add(grpAttr);
        stack.Children.Add(grpSec);
        stack.Children.Add(grpWpn);
        stack.Children.Add(grpDesc);
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
        UpdateFields();
    }

    private static string QualityFromDesc(string desc)
    {
        if (desc.Contains("Эпический")) return "Эпический";
        if (desc.Contains("Редкий")) return "Редкий";
        if (desc.Contains("Необычный")) return "Необычный";
        return "Обычный";
    }

    private void UpdateFields()
    {
        string t = _typeCombo.SelectedItem?.ToString() ?? "";
        bool isWeapon = t is "weapon" or "twohand";
        bool isArmor = t is "shield" or "helmet" or "cloak" or "chest" or "legs" or "boots" or "glove" or "belt";
        _minBox.IsEnabled = isWeapon;
        _maxBox.IsEnabled = isWeapon;
        _thBox.IsEnabled = isWeapon;
        _defBox.IsEnabled = isArmor;
        _hpBox.IsEnabled = isArmor || isWeapon || t is "consumable" or "trophy";
        _healBox.IsEnabled = t is "consumable" or "trophy" or "weapon" or "twohand";
        _manaBox.IsEnabled = t is "consumable";
        _dmgTypeBox.IsEnabled = isWeapon || t is "shield";
        _subtypeBox.IsEnabled = isWeapon || t is "shield";
        _asmBox.IsEnabled = isWeapon;
        _arBox.IsEnabled = isWeapon;
    }

    private void Save()
    {
        _row["id"] = _idBox.Text.Trim();
        _row["name"] = _nameBox.Text.Trim();
        _row["type"] = _typeCombo.SelectedItem?.ToString() ?? "";
        _row["required_level"] = Num(_reqBox);
        _row["value"] = Num(_valueBox);
        _row["stock"] = Num(_stockBox);
        _row["damage_min"] = Num(_minBox);
        _row["damage_max"] = Num(_maxBox);
        _row["defense"] = Num(_defBox);
        _row["max_health_bonus"] = Num(_hpBox);
        _row["heal_amount"] = Num(_healBox);
        _row["restore_mana"] = Num(_manaBox);
        _row["two_handed"] = _thBox.IsChecked == true ? 1 : 0;
        _row["bonus_strength"] = Num(_strBox);
        _row["bonus_endurance"] = Num(_endBox);
        _row["bonus_agility"] = Num(_agiBox);
        _row["bonus_cunning"] = Num(_cunBox);
        _row["bonus_intellect"] = Num(_intBox);
        _row["bonus_wisdom"] = Num(_wisBox);
        _row["bonus_phys_attack"] = Num(_bpaBox);
        _row["bonus_mag_attack"] = Num(_bmaBox);
        _row["bonus_defense"] = Num(_bdefBox);
        _row["bonus_resistance"] = Num(_bresBox);
        _row["bonus_attack_speed"] = Dbl(_basBox);
        _row["bonus_crit_chance"] = Dbl(_ccBox);
        _row["bonus_crit_damage"] = Dbl(_cdBox);
        _row["bonus_evade_chance"] = Dbl(_ecBox);
        _row["damage_type"] = _dmgTypeBox.Text;
        _row["weapon_subtype"] = _subtypeBox.Text;
        _row["attack_speed_modifier"] = Dbl(_asmBox);
        _row["attack_range"] = Num(_arBox);
        string qLabel = _qualityCombo.SelectedItem?.ToString() ?? "Обычный";
        string cleanDesc = RemoveQualityFromDesc(_descBox.Text);
        _row["description"] = $"Качество: {qLabel}. {cleanDesc}".TrimEnd('.', ' ');
        Close();
    }

    private static string RemoveQualityFromDesc(string desc)
    {
        var idx = desc.IndexOf("Качество:");
        if (idx < 0) return desc;
        int dotIdx = desc.IndexOf(". ", idx);
        if (dotIdx < 0) return desc[..idx].Trim();
        return (desc[..idx] + desc[(dotIdx + 2)..]).Trim();
    }

    // === helpers ===

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

    private ComboBox AddCombo(StackPanel g, string label, string[] items, string current)
    {
        var cb = new ComboBox { ItemsSource = items, Width = 180, HorizontalAlignment = HorizontalAlignment.Left };
        if (items.Contains(current)) cb.SelectedItem = current;
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
using System.Data;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace LostAndDivine.Editor.Views;

/// <summary>
/// Полноценный редактор предмета: все поля в группах, качество из описания.
/// Порт ItemEditForm (WinForms).
/// </summary>
public sealed class ItemEditorWindow : Window
{
    private readonly DataRow _row;
    private readonly DataTable _table;
    private Db _db = null!;

    private TextBox _idBox = null!, _nameBox = null!, _descBox = null!;
    private ComboBox _typeCombo = null!, _qualityCombo = null!, _dmgTypeBox = null!, _subtypeBox = null!;
    private TextBox _reqBox = null!, _valueBox = null!;
    private TextBox _minBox = null!, _maxBox = null!, _defBox = null!, _mdefBox = null!, _hpBox = null!, _mpBox = null!, _healBox = null!, _manaBox = null!;
    private CheckBox _thBox = null!;
    private TextBox _strBox = null!, _endBox = null!, _agiBox = null!, _cunBox = null!, _intBox = null!, _wisBox = null!;
    private TextBox _bpaBox = null!, _bmaBox = null!, _bdefBox = null!, _bresBox = null!, _basBox = null!, _ccBox = null!, _cdBox = null!, _ecBox = null!;
    private TextBox _blkBox = null!, _prrBox = null!, _accBox = null!, _tenBox = null!, _arpBox = null!, _cdrBox = null!, _hprBox = null!, _mprBox = null!;
    private TextBox _asmBox = null!, _arBox = null!;
    private TextBox _iconBox = null!;
    private Image _iconPreview = null!;
    private StackPanel _grpStats = null!, _grpAttr = null!, _grpSec = null!, _grpWpn = null!;

    public ItemEditorWindow(Db db, DataRow row, DataTable table)
    {
        _row = row;
        _table = table;
        _db = db;
        Title = "Редактирование: " + Cell("name");
        Width = 560;
        Height = 760;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var stack = new StackPanel { Margin = new Thickness(12) };

        var grpMain = Group("Основное");
        _idBox = AddText(grpMain, "ID:", Cell("id"));
        _nameBox = AddText(grpMain, "Имя:", Cell("name"));
        _typeCombo = new ComboBox
        {
            ItemsSource = Ui.ItemTypesLocalized,
            DisplayMemberPath = "Value",
            SelectedValuePath = "Key",
            Width = 180,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        string currentType = Cell("type");
        if (Ui.ItemTypesLocalized.Any(p => p.Key == currentType)) _typeCombo.SelectedValue = currentType;
        AddRow(grpMain, "Тип:", _typeCombo);
        _qualityCombo = AddCombo(grpMain, "Качество:", new[] { "Обычный", "Необычный", "Редкий", "Эпический" }, QualityFromDesc(Cell("description")));
        _reqBox = AddNum(grpMain, "Треб. уровень:", Cell("required_level"));
        _valueBox = AddNum(grpMain, "Цена:", Cell("value"));

        // Иконка предмета (копируется в Content/Sprites/CustomIcons клиента)
        var iconRow = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        iconRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        iconRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var iconLbl = new TextBlock { Text = "Иконка:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        Grid.SetColumn(iconLbl, 0);
        _iconBox = new TextBox { Text = Cell("icon"), Width = 120, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        var browseBtn = new Button { Content = "Обзор…", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        _iconPreview = new Image { Width = 32, Height = 32, Margin = new Thickness(6, 0, 0, 0), Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        browseBtn.Click += (s, e) => BrowseIcon();
        var iconPanel = new StackPanel { Orientation = Orientation.Horizontal };
        iconPanel.Children.Add(_iconBox);
        iconPanel.Children.Add(browseBtn);
        iconPanel.Children.Add(_iconPreview);
        Grid.SetColumn(iconPanel, 1);
        iconRow.Children.Add(iconLbl);
        iconRow.Children.Add(iconPanel);
        grpMain.Children.Add(iconRow);

        _grpStats = Group("Характеристики");
        var grpStats = _grpStats;
        _minBox = AddNum(grpStats, "Урон мин:", Cell("damage_min"));
        _maxBox = AddNum(grpStats, "Урон макс:", Cell("damage_max"));
        _defBox = AddNum(grpStats, "Физ. защита:", Cell("defense"));
        _mdefBox = AddNum(grpStats, "Маг. защита:", Cell("magic_defense"));
        _hpBox = AddNum(grpStats, "Бонус к HP:", Cell("max_health_bonus"));
        _mpBox = AddNum(grpStats, "Бонус к MP:", Cell("max_mana_bonus"));
        _healBox = AddNum(grpStats, "Лечение:", Cell("heal_amount"));
        _manaBox = AddNum(grpStats, "Восст. маны:", Cell("restore_mana"));
        _thBox = AddCheck(grpStats, "Двуручное:", Db.ToInt(Cell("two_handed")) != 0);

        _grpAttr = Group("Атрибуты");
        var grpAttr = _grpAttr;
        _strBox = AddNum(grpAttr, "Сила:", Cell("bonus_strength"));
        _endBox = AddNum(grpAttr, "Выносливость:", Cell("bonus_endurance"));
        _agiBox = AddNum(grpAttr, "Ловкость:", Cell("bonus_agility"));
        _cunBox = AddNum(grpAttr, "Хитрость:", Cell("bonus_cunning"));
        _intBox = AddNum(grpAttr, "Интеллект:", Cell("bonus_intellect"));
        _wisBox = AddNum(grpAttr, "Мудрость:", Cell("bonus_wisdom"));

        _grpSec = Group("Доп. характеристики");
        var grpSec = _grpSec;
        _bpaBox = AddNum(grpSec, "+Физ. атака:", Cell("bonus_phys_attack"));
        _bmaBox = AddNum(grpSec, "+Маг. атака:", Cell("bonus_mag_attack"));
        _bdefBox = AddNum(grpSec, "+Физ. защита:", Cell("bonus_defense"));
        _bresBox = AddNum(grpSec, "+Маг. защита:", Cell("bonus_resistance"));
        _basBox = AddNum(grpSec, "+Скор. атк %:", Cell("bonus_attack_speed"));
        _ccBox = AddNum(grpSec, "+Крит %:", Cell("bonus_crit_chance"));
        _cdBox = AddNum(grpSec, "+Крит урон %:", Cell("bonus_crit_damage"));
        _ecBox = AddNum(grpSec, "+Уклон %:", Cell("bonus_evade_chance"));
        _blkBox = AddNum(grpSec, "+Блок %:", Cell("bonus_block_chance"));
        _prrBox = AddNum(grpSec, "+Парир %:", Cell("bonus_parry_chance"));
        _accBox = AddNum(grpSec, "+Точность %:", Cell("bonus_accuracy"));
        _tenBox = AddNum(grpSec, "+Стойк %:", Cell("bonus_tenacity"));
        _arpBox = AddNum(grpSec, "+Пробив %:", Cell("bonus_armor_penetration"));
        _cdrBox = AddNum(grpSec, "+Откат %:", Cell("bonus_cooldown_reduction"));
        _hprBox = AddNum(grpSec, "+Реген ХП %:", Cell("bonus_hp_regen"));
        _mprBox = AddNum(grpSec, "+Реген МП %:", Cell("bonus_mp_regen"));

        _grpWpn = Group("Оружие");
        var grpWpn = _grpWpn;
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
        _subtypeBox.SelectionChanged += (s, e) => UpdateFields();
        UpdateFields();
        LoadIconPreview(Cell("icon"));
    }

    /// <summary>Выбор PNG-файла иконки: копирует его в Content/Sprites/CustomIcons клиента и запоминает ключ.</summary>
    private void BrowseIcon()
    {
        var dlg = new OpenFileDialog { Title = "Выберите иконку предмета (PNG)", Filter = "PNG (*.png)|*.png" };
        if (dlg.ShowDialog() != true) return;
        string name = Path.GetFileName(dlg.FileName);
        string key = Path.GetFileNameWithoutExtension(name);
        if (string.IsNullOrWhiteSpace(key)) return;
        try
        {
            var dirs = new List<string>
            {
                Path.Combine(_db.ClientSrcContent(), "Sprites", "CustomIcons"),
                Path.Combine(_db.ClientBinContent(), "Sprites", "CustomIcons"),
            };
            string binRoot = Path.GetFullPath(Path.Combine(_db.ClientBinContent(), "..", "..", ".."));
            dirs.Add(Path.Combine(binRoot, "Release", "net8.0", "Content", "Sprites", "CustomIcons"));
            foreach (var dir in dirs.Distinct())
            {
                Directory.CreateDirectory(dir);
                File.Copy(dlg.FileName, Path.Combine(dir, name), true);
            }
            _iconBox.Text = key;
            LoadIconPreview(key);
        }
        catch (Exception ex) { MessageBox.Show("Не удалось скопировать иконку: " + ex.Message, "Иконка", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void LoadIconPreview(string key)
    {
        try
        {
            var path = Path.Combine(_db.ClientSrcContent(), "Sprites", "CustomIcons", key + ".png");
            if (!File.Exists(path))
            {
                _iconPreview.Visibility = Visibility.Collapsed;
                return;
            }
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            _iconPreview.Source = bmp;
            _iconPreview.Visibility = Visibility.Visible;
        }
        catch { _iconPreview.Visibility = Visibility.Collapsed; }
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
        string t = _typeCombo.SelectedValue?.ToString() ?? "";
        bool isWeapon = t is "weapon" or "twohand";
        bool isDefGear = t is "shield" or "helmet" or "cloak" or "chest" or "legs" or "boots" or "glove" or "belt"
            or "necklace" or "ring";
        string subtype = _subtypeBox.SelectedItem?.ToString() ?? "";

        SetRowVisible(_minBox, isWeapon);
        SetRowVisible(_maxBox, isWeapon);
        SetRowVisible(_thBox, isWeapon);
        SetRowVisible(_defBox, isDefGear);
        SetRowVisible(_mdefBox, isDefGear);
        SetRowVisible(_hpBox, isDefGear);
        SetRowVisible(_mpBox, isDefGear);
        SetRowVisible(_healBox, t is "consumable");
        SetRowVisible(_manaBox, t is "consumable");
        _grpStats.Visibility = isWeapon || isDefGear || t is "consumable" ? Visibility.Visible : Visibility.Collapsed;

        SetRowVisible(_dmgTypeBox, isWeapon);
        SetRowVisible(_subtypeBox, isWeapon);
        SetRowVisible(_asmBox, isWeapon);
        SetRowVisible(_arBox, isWeapon && subtype is "bow" or "staff");
        _grpWpn.Visibility = isWeapon ? Visibility.Visible : Visibility.Collapsed;

        bool equippable = isWeapon || isDefGear;
        _grpAttr.Visibility = equippable ? Visibility.Visible : Visibility.Collapsed;
        _grpSec.Visibility = equippable ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void SetRowVisible(Control c, bool on)
    {
        if (c.Parent is FrameworkElement row)
            row.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Save()
    {
        _row["id"] = _idBox.Text.Trim();
        _row["name"] = _nameBox.Text.Trim();
        _row["type"] = _typeCombo.SelectedValue?.ToString() ?? "";
        _row["required_level"] = Num(_reqBox);
        _row["value"] = Num(_valueBox);
        _row["damage_min"] = Num(_minBox);
        _row["damage_max"] = Num(_maxBox);
        _row["defense"] = Num(_defBox);
        _row["magic_defense"] = Num(_mdefBox);
        _row["max_health_bonus"] = Num(_hpBox);
        _row["max_mana_bonus"] = Num(_mpBox);
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
        _row["bonus_block_chance"] = Dbl(_blkBox);
        _row["bonus_parry_chance"] = Dbl(_prrBox);
        _row["bonus_accuracy"] = Dbl(_accBox);
        _row["bonus_tenacity"] = Dbl(_tenBox);
        _row["bonus_armor_penetration"] = Dbl(_arpBox);
        _row["bonus_cooldown_reduction"] = Dbl(_cdrBox);
        _row["bonus_hp_regen"] = Dbl(_hprBox);
        _row["bonus_mp_regen"] = Dbl(_mprBox);
        _row["damage_type"] = _dmgTypeBox.Text;
        _row["weapon_subtype"] = _subtypeBox.Text;
        _row["attack_speed_modifier"] = Dbl(_asmBox);
        _row["attack_range"] = Num(_arBox);
        _row["icon"] = _iconBox.Text.Trim();
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
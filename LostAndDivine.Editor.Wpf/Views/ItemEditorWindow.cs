using System.Data;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LostAndDivine.Shared.Models;
using Microsoft.Win32;

namespace LostAndDivine.Editor.Views;

/// <summary>
/// Полноценный редактор предмета: все поля в группах, качество в отдельной колонке.
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
    private StackPanel _grpStats = null!, _grpAttr = null!, _grpSec = null!, _grpWpn = null!, _grpRoll = null!;
    private CheckBox _rollEnabledBox = null!;
    private TextBox _uncWeightBox = null!, _rareWeightBox = null!, _epicWeightBox = null!;
    private TextBox _uncMinBox = null!, _uncMaxBox = null!;
    private TextBox _rareMinBox = null!, _rareMaxBox = null!;
    private TextBox _epicMinBox = null!, _epicMaxBox = null!;
    private DataGrid _rollGrid = null!;
    private DataTable _rollDt = new();

    public ItemEditorWindow(Db db, DataRow row, DataTable table)
    {
        _row = row;
        _table = table;
        _db = db;
        Title = "Редактирование: " + Cell("name");
        Width = 940;
        Height = 820;
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
        _qualityCombo = AddCombo(grpMain, "Качество:", new[] { "Обычный", "Необычный", "Редкий", "Эпический" }, QualityFromInt(Cell("quality")));
        _reqBox = AddNum(grpMain, "Треб. уровень:", Cell("required_level"));
        _valueBox = AddNum(grpMain, "Цена:", Cell("value"));

        // Иконка предмета (копируется в Content/Sprites/CustomIcons клиента)
        var iconRow = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        iconRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
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

        var grpRoll = Group("Случайные бонусы (ролл при дропе)");
        _rollEnabledBox = AddCheck(grpRoll, "Включить ролл бонусов:", false);
        _rollEnabledBox.Checked += (s, e) => ApplyDefaultRolls();
        AddWeightRow(grpRoll, "Веса (Необ/Ред/Эпик):", out _uncWeightBox, out _rareWeightBox, out _epicWeightBox);
        AddCountRow(grpRoll, "Необычный: бонусов", out _uncMinBox, out _uncMaxBox);
        AddCountRow(grpRoll, "Редкий: бонусов", out _rareMinBox, out _rareMaxBox);
        AddCountRow(grpRoll, "Эпический: бонусов", out _epicMinBox, out _epicMaxBox);
        BuildRollTable();
        _reqBox.TextChanged += (s, e) => RecomputeTotals();
        var rollBtns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        var copyBtn = new Button { Content = "Копировать строку", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 8, 0) };
        copyBtn.Click += (s, e) => CopyRollRow();
        var pasteBtn = new Button { Content = "Вставить в строку", Padding = new Thickness(8, 2, 8, 2) };
        pasteBtn.Click += (s, e) => PasteRollRow();
        rollBtns.Children.Add(copyBtn);
        rollBtns.Children.Add(pasteBtn);
        grpRoll.Children.Add(rollBtns);
        grpRoll.Children.Add(_rollGrid);
        grpRoll.Children.Add(new TextBlock
        {
            Text = "Веса — относительные шансы качества (остаток до 100% — Обычный). Пример: 30/15/5 → 50% обычный, 30% необычный, 15% редкий, 5% эпический.\n" +
                   "Значения в таблице — бонус за уровень предмета (умножаются на требуемый уровень). " +
                   "Минимум и максимум равны 0/пусты — параметр не участвует в ролле.\n" +
                   "Колонки «итог» — итоговые значения при выпадении: мин/макс × требуемый уровень.\n" +
                   "Статичные бонусы шаблона — база Обычного качества: торговец всегда продаёт именно эту версию, " +
                   "и при выпадении Обычного дропом предмет остаётся как в шаблоне. " +
                   "При Необычном и выше статичные бонусы заменяются своролленными.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(new Color { R = 120, G = 125, B = 140, A = 255 }),
            Margin = new Thickness(0, 4, 0, 0)
        });
        _grpRoll = grpRoll;

        var grpDesc = Group("Описание");
        _descBox = new TextBox { Text = Cell("description"), Height = 70, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        grpDesc.Children.Add(_descBox);

        stack.Children.Add(grpMain);
        stack.Children.Add(grpStats);
        stack.Children.Add(grpAttr);
        stack.Children.Add(grpSec);
        stack.Children.Add(grpRoll);
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
        LoadRollConfig();
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

    private static string QualityFromInt(string v) => Db.ToInt(v) switch
    {
        1 => "Необычный",
        2 => "Редкий",
        3 => "Эпический",
        _ => "Обычный"
    };

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
        _grpRoll.Visibility = equippable ? Visibility.Visible : Visibility.Collapsed;
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
        _row["quality"] = (_qualityCombo.SelectedItem?.ToString()) switch
        {
            "Необычный" => 1,
            "Редкий" => 2,
            "Эпический" => 3,
            _ => 0
        };
        _row["description"] = _descBox.Text.Trim();
        _row["roll_config"] = BuildRollConfigJson();
        Close();
    }

    // === случайные бонусы (roll_config) ===

    private string?[] _copiedRoll = new string?[6];

    private DataRow? SelectedRollRow()
    {
        if (_rollGrid.SelectedItem is DataRowView view) return view.Row;
        return null;
    }

    private void CopyRollRow()
    {
        _rollGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        var row = SelectedRollRow();
        if (row == null)
        {
            MessageBox.Show("Выберите строку с параметром, который нужно скопировать.", "Копирование", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _copiedRoll = new[]
        {
            row["unc_min"]?.ToString(), row["unc_max"]?.ToString(),
            row["rare_min"]?.ToString(), row["rare_max"]?.ToString(),
            row["epic_min"]?.ToString(), row["epic_max"]?.ToString()
        };
    }

    private void PasteRollRow()
    {
        _rollGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        var row = SelectedRollRow();
        if (row == null)
        {
            MessageBox.Show("Выберите строку, в которую нужно вставить значения.", "Вставка", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (_copiedRoll.All(string.IsNullOrEmpty))
        {
            MessageBox.Show("Сначала скопируйте строку (кнопка «Копировать строку»).", "Вставка", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        row["unc_min"] = _copiedRoll[0];
        row["unc_max"] = _copiedRoll[1];
        row["rare_min"] = _copiedRoll[2];
        row["rare_max"] = _copiedRoll[3];
        row["epic_min"] = _copiedRoll[4];
        row["epic_max"] = _copiedRoll[5];
        RecomputeTotals();
    }

    /// <summary>При включении ролла заполняет пустые поля значениями по умолчанию (30/15/5, 1-2/2-3/3-4).</summary>
    private void ApplyDefaultRolls()
    {
        if (_rollEnabledBox.IsChecked != true) return;
        FillIfEmpty(_uncWeightBox, "30");
        FillIfEmpty(_rareWeightBox, "15");
        FillIfEmpty(_epicWeightBox, "5");
        FillIfEmpty(_uncMinBox, "1");
        FillIfEmpty(_uncMaxBox, "2");
        FillIfEmpty(_rareMinBox, "2");
        FillIfEmpty(_rareMaxBox, "3");
        FillIfEmpty(_epicMinBox, "3");
        FillIfEmpty(_epicMaxBox, "4");
    }

    private static void FillIfEmpty(TextBox tb, string value)
    {
        if (string.IsNullOrWhiteSpace(tb.Text)) tb.Text = value;
    }

    /// <summary>Строка «[метка]: [необ] [ред] [эпик]» — веса качества при дропе.</summary>
    private static void AddWeightRow(StackPanel g, string label, out TextBox uncBox, out TextBox rareBox, out TextBox epicBox)
    {
        uncBox = new TextBox { Width = 50, HorizontalAlignment = HorizontalAlignment.Left };
        rareBox = new TextBox { Width = 50, HorizontalAlignment = HorizontalAlignment.Left };
        epicBox = new TextBox { Width = 50, HorizontalAlignment = HorizontalAlignment.Left };
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
        var lbl = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        Grid.SetColumn(lbl, 0);
        Grid.SetColumn(uncBox, 1);
        Grid.SetColumn(rareBox, 2);
        Grid.SetColumn(epicBox, 3);
        grid.Children.Add(lbl);
        grid.Children.Add(uncBox);
        grid.Children.Add(rareBox);
        grid.Children.Add(epicBox);
        g.Children.Add(grid);
    }

    /// <summary>Строка «[метка]: от [мин] до [макс]» — число случайных бонусов для качества.</summary>
    private static void AddCountRow(StackPanel g, string label, out TextBox minBox, out TextBox maxBox)
    {
        minBox = new TextBox { Width = 50, HorizontalAlignment = HorizontalAlignment.Left };
        maxBox = new TextBox { Width = 50, HorizontalAlignment = HorizontalAlignment.Left };
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
        var lbl = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        var dash = new TextBlock { Text = "до", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) };
        Grid.SetColumn(lbl, 0);
        Grid.SetColumn(minBox, 1);
        Grid.SetColumn(dash, 2);
        Grid.SetColumn(maxBox, 3);
        grid.Children.Add(lbl);
        grid.Children.Add(minBox);
        grid.Children.Add(dash);
        grid.Children.Add(maxBox);
        g.Children.Add(grid);
    }

private void BuildRollTable()
    {
        _rollDt = new DataTable();
        _rollDt.Columns.Add("key", typeof(string));
        _rollDt.Columns.Add("label", typeof(string));
        _rollDt.Columns.Add("unc_min", typeof(string));
        _rollDt.Columns.Add("unc_max", typeof(string));
        _rollDt.Columns.Add("unc_total", typeof(string));
        _rollDt.Columns.Add("rare_min", typeof(string));
        _rollDt.Columns.Add("rare_max", typeof(string));
        _rollDt.Columns.Add("rare_total", typeof(string));
        _rollDt.Columns.Add("epic_min", typeof(string));
        _rollDt.Columns.Add("epic_max", typeof(string));
        _rollDt.Columns.Add("epic_total", typeof(string));
        foreach (var (key, label) in RollStatCatalog.All)
            _rollDt.Rows.Add(key, label, "", "", "", "", "", "", "", "", "");

        _rollGrid = new DataGrid
        {
            ItemsSource = _rollDt.DefaultView,
            AutoGenerateColumns = false,
            Height = 240,
            RowHeaderWidth = 0,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        _rollGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Параметр",
            Binding = new Binding("label"),
            IsReadOnly = true,
            Width = new DataGridLength(180)
        });
        AddRollColumn("Необ. мин", "unc_min", 70);
        AddRollColumn("Необ. макс", "unc_max", 70);
        AddRollColumn("Необ. итог", "unc_total", 85, readOnly: true);
        AddRollColumn("Ред. мин", "rare_min", 70);
        AddRollColumn("Ред. макс", "rare_max", 70);
        AddRollColumn("Ред. итог", "rare_total", 85, readOnly: true);
        AddRollColumn("Эпик. мин", "epic_min", 70);
        AddRollColumn("Эпик. макс", "epic_max", 70);
        AddRollColumn("Эпик. итог", "epic_total", 85, readOnly: true);
        _rollGrid.CellEditEnding += (s, e) => RecomputeTotals();
    }

    private void AddRollColumn(string header, string column, double width = 85, bool readOnly = false)
    {
        _rollGrid.Columns.Add(new DataGridTextColumn
        {
            Header = header,
            Binding = new Binding(column) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
            IsReadOnly = readOnly,
            Width = new DataGridLength(width)
        });
    }

    /// <summary>Итоговые значения при выпадении: мин/макс за уровень × требуемый уровень.</summary>
    private void RecomputeTotals()
    {
        int level = Math.Max(1, Num(_reqBox));
        foreach (DataRow row in _rollDt.Rows)
        {
            SetTotal(row, "unc", level);
            SetTotal(row, "rare", level);
            SetTotal(row, "epic", level);
        }
    }

    private static void SetTotal(DataRow row, string prefix, int level)
    {
        double min = Dbl(row[prefix + "_min"]?.ToString() ?? "");
        double max = Dbl(row[prefix + "_max"]?.ToString() ?? "");
        if (min <= 0 && max <= 0)
        {
            row[prefix + "_total"] = "";
            return;
        }
        bool pct = RollStatCatalog.IsPercentStat(row["key"]?.ToString() ?? "");
        string a = pct
            ? FormatNum(Math.Round(min * level, 1, MidpointRounding.AwayFromZero))
            : ((int)Math.Round(min * level, MidpointRounding.AwayFromZero)).ToString();
        string b = pct
            ? FormatNum(Math.Round(max * level, 1, MidpointRounding.AwayFromZero))
            : ((int)Math.Round(max * level, MidpointRounding.AwayFromZero)).ToString();
        row[prefix + "_total"] = a == b ? a : a + "–" + b;
    }

    private void AddRollColumn(string header, string column)
    {
        _rollGrid.Columns.Add(new DataGridTextColumn
        {
            Header = header,
            Binding = new Binding(column) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
            Width = new DataGridLength(85)
        });
    }

    private void LoadRollConfig()
    {
        ItemRollConfig? cfg = null;
        string json = Cell("roll_config");
        if (!string.IsNullOrWhiteSpace(json))
        {
            try { cfg = JsonSerializer.Deserialize<ItemRollConfig>(json, ItemRollConfig.JsonOpts); }
            catch { cfg = null; }
        }

        _rollEnabledBox.IsChecked = cfg is { Enabled: true };
        if (cfg == null) return;

        _uncWeightBox.Text = cfg.WeightUncommon.ToString();
        _rareWeightBox.Text = cfg.WeightRare.ToString();
        _epicWeightBox.Text = cfg.WeightEpic.ToString();
        _uncMinBox.Text = cfg.Uncommon.CountMin.ToString();
        _uncMaxBox.Text = cfg.Uncommon.CountMax.ToString();
        ApplyTierToTable(cfg.Uncommon, "unc");
        _rareMinBox.Text = cfg.Rare.CountMin.ToString();
        _rareMaxBox.Text = cfg.Rare.CountMax.ToString();
        ApplyTierToTable(cfg.Rare, "rare");
        _epicMinBox.Text = cfg.Epic.CountMin.ToString();
        _epicMaxBox.Text = cfg.Epic.CountMax.ToString();
        ApplyTierToTable(cfg.Epic, "epic");
        RecomputeTotals();
    }

    private void ApplyTierToTable(RollTierConfig tier, string prefix)
    {
        foreach (DataRow row in _rollDt.Rows)
        {
            var stat = tier.Stats.FirstOrDefault(s => s.Stat == row["key"]?.ToString());
            if (stat == null) continue;
            row[prefix + "_min"] = FormatNum(stat.Min);
            row[prefix + "_max"] = FormatNum(stat.Max);
        }
    }

    private string BuildRollConfigJson()
    {
        _rollGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        if (_rollEnabledBox.IsChecked != true) return "";
        var cfg = new ItemRollConfig
        {
            Enabled = true,
            WeightUncommon = Num(_uncWeightBox),
            WeightRare = Num(_rareWeightBox),
            WeightEpic = Num(_epicWeightBox)
        };
        cfg.Uncommon = BuildTier(_uncMinBox, _uncMaxBox, "unc");
        cfg.Rare = BuildTier(_rareMinBox, _rareMaxBox, "rare");
        cfg.Epic = BuildTier(_epicMinBox, _epicMaxBox, "epic");
        return JsonSerializer.Serialize(cfg, ItemRollConfig.JsonOpts);
    }

    private RollTierConfig BuildTier(TextBox minBox, TextBox maxBox, string prefix)
    {
        var tier = new RollTierConfig
        {
            CountMin = Num(minBox),
            CountMax = Num(maxBox)
        };
        foreach (DataRow row in _rollDt.Rows)
        {
            double min = Dbl(row[prefix + "_min"]?.ToString() ?? "");
            double max = Dbl(row[prefix + "_max"]?.ToString() ?? "");
            if (min <= 0 && max <= 0) continue;
            tier.Stats.Add(new RollStatConfig
            {
                Stat = row["key"]?.ToString() ?? "",
                Min = min,
                Max = Math.Max(max, min)
            });
        }
        return tier;
    }

    private static string FormatNum(double v)
    {
        if (v == Math.Floor(v)) return ((long)v).ToString();
        return v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static double Dbl(string v)
    {
        if (double.TryParse(v, System.Globalization.CultureInfo.CurrentCulture, out double r)) return r;
        if (double.TryParse(v, System.Globalization.CultureInfo.InvariantCulture, out r)) return r;
        return 0;
    }

    // === helpers ===

    private string Cell(string col) => _row[col]?.ToString() ?? "";
    private static int Num(TextBox tb) => int.TryParse(tb.Text, out int v) ? v : 0;
    private static double Dbl(TextBox tb)
    {
        string v = tb.Text;
        if (double.TryParse(v, System.Globalization.CultureInfo.CurrentCulture, out double r)) return r;
        if (double.TryParse(v, System.Globalization.CultureInfo.InvariantCulture, out r)) return r;
        return 0;
    }

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
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var lbl = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        Grid.SetColumn(lbl, 0);
        Grid.SetColumn(control, 1);
        grid.Children.Add(lbl);
        grid.Children.Add(control);
        g.Children.Add(grid);
    }
}
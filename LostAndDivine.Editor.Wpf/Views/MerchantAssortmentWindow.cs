using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace LostAndDivine.Editor.Views;

/// <summary>
/// Ассортимент торговца: список предметов слева, выбранные товары справа.
/// Запас (сколько единиц в наличии у торговца) задаётся здесь же, per-мерчант,
/// в колонке «Запас» правой таблицы. Порт MerchantAssortmentEditorForm.
/// </summary>
public sealed class MerchantAssortmentWindow : Window
{
    private readonly Db _db;
    private readonly string _npcId;

    private TextBox _searchBox = null!;
    private ListBox _allItems = null!;
    private DataGrid _stockGrid = null!;
    private readonly ObservableCollection<StockRow> _stock = new();
    private readonly List<(string Id, string Name, string Type)> _items = new();
    private readonly Dictionary<string, int> _itemStock = new();

    public MerchantAssortmentWindow(Db db, string npcId, string npcName)
    {
        _db = db;
        _npcId = npcId;
        Title = $"Ассортимент торговца {npcId} ({npcName})";
        Width = 820;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var grid = new Grid { Margin = new Thickness(8) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _searchBox = new TextBox { Margin = new Thickness(0, 0, 0, 6) };
        _searchBox.TextChanged += (s, e) => ApplyFilter();
        Grid.SetRow(_searchBox, 0);

        var split = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });

        var leftPanel = new DockPanel();
        var leftHint = new TextBlock { Text = "Все предметы (двойной клик — в ассортимент)", Margin = new Thickness(0, 0, 0, 3), Foreground = System.Windows.Media.Brushes.Gray };
        DockPanel.SetDock(leftHint, Dock.Top);
        _allItems = new ListBox();
        _allItems.MouseDoubleClick += (s, e) => AddSelected();
        leftPanel.Children.Add(_allItems);
        leftPanel.Children.Add(leftHint);

        var rightPanel = new DockPanel();
        var rightHint = new TextBlock { Text = "Товары торговца (двойной клик по строке — убрать; «Запас» — сколько в наличии)", Margin = new Thickness(0, 0, 0, 3), Foreground = System.Windows.Media.Brushes.Gray };
        DockPanel.SetDock(rightHint, Dock.Top);
        _stockGrid = new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            SelectionMode = DataGridSelectionMode.Single,
            SelectionUnit = DataGridSelectionUnit.FullRow,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            RowHeaderWidth = 0
        };
        _stockGrid.Columns.Add(new DataGridTextColumn { Header = "Товар", Binding = new Binding("Name"), IsReadOnly = true, Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
        _stockGrid.Columns.Add(new DataGridTextColumn { Header = "Запас", Binding = new Binding("Stock") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = new DataGridLength(70) });
        _stockGrid.ItemsSource = _stock;
        _stockGrid.MouseDoubleClick += (s, e) => RemoveSelected();
        rightPanel.Children.Add(_stockGrid);
        rightPanel.Children.Add(rightHint);

        Grid.SetColumn(leftPanel, 0);
        Grid.SetColumn(rightPanel, 1);
        split.Children.Add(leftPanel);
        split.Children.Add(rightPanel);
        Grid.SetRow(split, 1);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var okBtn = new Button { Content = "Сохранить", Width = 110, Height = 30, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 8, 0) };
        okBtn.Click += (s, e) => Save();
        var cancelBtn = new Button { Content = "Отмена", Width = 100, Height = 30 };
        cancelBtn.Click += (s, e) => Close();
        buttons.Children.Add(okBtn);
        buttons.Children.Add(cancelBtn);
        Grid.SetRow(buttons, 2);

        grid.Children.Add(_searchBox);
        grid.Children.Add(split);
        grid.Children.Add(buttons);
        Content = grid;

        LoadItems();
        LoadStock();
        ApplyFilter();
    }

    private void LoadItems()
    {
        _items.Clear();
        _itemStock.Clear();
        using var conn = _db.OpenContent();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, type, stock FROM items WHERE type <> 'collectible' ORDER BY id";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            _items.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
            _itemStock[reader.GetString(0)] = Db.ToInt(reader.GetValue(3));
        }
    }

    private void LoadStock()
    {
        _stock.Clear();
        using var conn = _db.OpenContent();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT ms.item_id, i.name, i.type, ms.stock FROM merchant_stock ms LEFT JOIN items i ON i.id = ms.item_id WHERE ms.npc_id = $npc ORDER BY i.name";
        cmd.Parameters.AddWithValue("$npc", _npcId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            _stock.Add(new StockRow
            {
                Id = reader.GetString(0),
                Name = reader.IsDBNull(1) ? reader.GetString(0) : reader.GetString(1),
                Type = reader.IsDBNull(2) ? "" : reader.GetString(2),
                Stock = Db.ToInt(reader.GetValue(3))
            });
        }
    }

    private void ApplyFilter()
    {
        string f = _searchBox.Text.Trim().ToLowerInvariant();
        var filtered = string.IsNullOrWhiteSpace(f)
            ? _items
            : _items.Where(i => i.Name.ToLowerInvariant().Contains(f) || i.Id.ToLowerInvariant().Contains(f)).ToList();
        _allItems.ItemsSource = filtered.Select(i => $"{i.Id}  —  {i.Name}  [{i.Type}]").ToList();
    }

    private void AddSelected()
    {
        if (_allItems.SelectedItem is not string line) return;
        int sep = line.IndexOf("  —  ");
        if (sep < 0) return;
        string id = line[..sep].Trim();
        if (_stock.Any(r => r.Id == id)) return;
        var info = _items.FirstOrDefault(i => i.Id == id);
        _stock.Add(new StockRow
        {
            Id = id,
            Name = info.Name,
            Type = info.Type,
            Stock = Math.Max(1, _itemStock.TryGetValue(id, out int s) ? s : 1)
        });
    }

    private void RemoveSelected()
    {
        if (_stockGrid.SelectedItem is StockRow row) _stock.Remove(row);
    }

    private void Save()
    {
        try
        {
            using var conn = _db.OpenContent();
            using var transaction = conn.BeginTransaction();
            using (var del = conn.CreateCommand())
            {
                del.CommandText = "DELETE FROM merchant_stock WHERE npc_id = $npc";
                del.Parameters.AddWithValue("$npc", _npcId);
                del.ExecuteNonQuery();
            }
            foreach (var row in _stock)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO merchant_stock (npc_id, item_id, stock) VALUES ($npc, $item, $stock)";
                cmd.Parameters.AddWithValue("$npc", _npcId);
                cmd.Parameters.AddWithValue("$item", row.Id);
                cmd.Parameters.AddWithValue("$stock", Math.Max(1, row.Stock));
                cmd.ExecuteNonQuery();
            }
            transaction.Commit();
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Не удалось сохранить ассортимент:\n" + ex.Message,
                "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private sealed class StockRow
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public int Stock { get; set; } = 1;
    }
}
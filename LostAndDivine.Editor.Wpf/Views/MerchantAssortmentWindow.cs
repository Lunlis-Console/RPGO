using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace LostAndDivine.Editor.Views;

/// <summary>Ассортимент торговца: список предметов слева, выбранные товары справа. Порт MerchantAssortmentEditorForm.</summary>
public sealed class MerchantAssortmentWindow : Window
{
    private readonly Db _db;
    private readonly string _npcId;

    private TextBox _searchBox = null!;
    private ListBox _allItems = null!;
    private ListBox _stockItems = null!;
    private readonly ObservableCollection<string> _stock = new();
    private readonly List<(string Id, string Name, string Type)> _items = new();

    public MerchantAssortmentWindow(Db db, string npcId, string npcName)
    {
        _db = db;
        _npcId = npcId;
        Title = $"Ассортимент торговца {npcId} ({npcName})";
        Width = 720;
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
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var leftPanel = new DockPanel();
        var leftHint = new TextBlock { Text = "Все предметы (двойной клик — в ассортимент)", Margin = new Thickness(0, 0, 0, 3), Foreground = System.Windows.Media.Brushes.Gray };
        DockPanel.SetDock(leftHint, Dock.Top);
        _allItems = new ListBox();
        _allItems.MouseDoubleClick += (s, e) => AddSelected();
        leftPanel.Children.Add(_allItems);
        leftPanel.Children.Add(leftHint);

        var rightPanel = new DockPanel();
        var rightHint = new TextBlock { Text = "Товары торговца (двойной клик — убрать)", Margin = new Thickness(0, 0, 0, 3), Foreground = System.Windows.Media.Brushes.Gray };
        DockPanel.SetDock(rightHint, Dock.Top);
        _stockItems = new ListBox { ItemsSource = _stock };
        _stockItems.MouseDoubleClick += (s, e) => RemoveSelected();
        rightPanel.Children.Add(_stockItems);
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
        using var conn = _db.OpenContent();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, type FROM items WHERE type <> 'collectible' ORDER BY id";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            _items.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
    }

    private void LoadStock()
    {
        _stock.Clear();
        using var conn = _db.OpenContent();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT item_id FROM merchant_stock WHERE npc_id = $npc";
        cmd.Parameters.AddWithValue("$npc", _npcId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            _stock.Add(reader.GetString(0));
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
        if (!_stock.Contains(id)) _stock.Add(id);
    }

    private void RemoveSelected()
    {
        if (_stockItems.SelectedItem is string id) _stock.Remove(id);
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
            foreach (var itemId in _stock)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT OR IGNORE INTO merchant_stock (npc_id, item_id) VALUES ($npc, $item)";
                cmd.Parameters.AddWithValue("$npc", _npcId);
                cmd.Parameters.AddWithValue("$item", itemId);
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
}
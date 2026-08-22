using System.Data;
using System.Windows;
using System.Windows.Controls;
using LostAndDivine.Shared.Data;

namespace LostAndDivine.Editor.Views;

/// <summary>Полный редактор NPC. Двойной клик в таблице открывает это окно.</summary>
public sealed class NpcEditorWindow : Window
{
    private readonly DataRow _row;
    private readonly Action<string> _status;
    private readonly Action<DataRow> _openDialogue;
    private readonly Action<DataRow> _placeOnMap;
    private readonly Action<DataRow> _duplicateNpc;

    private TextBox _idBox = null!, _nameBox = null!, _locationBox = null!, _radiusBox = null!;
    private ComboBox _typeBox = null!;

    public NpcEditorWindow(Db db, DataRow row, Action<string> status, Action<DataRow> openDialogue, Action<DataRow> placeOnMap, Action<DataRow> duplicateNpc)
    {
        _row = row;
        _status = status;
        _openDialogue = openDialogue;
        _placeOnMap = placeOnMap;
        _duplicateNpc = duplicateNpc;
        Title = "NPC: " + Cell("name") + " [" + Cell("id") + "]";
        Width = 580;
        Height = 420;
        FontSize = 14;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var stack = new StackPanel { Margin = new Thickness(12) };

        var grp = Group("Основное");
        _idBox = AddText(grp, "ID:", Cell("id"));
        _nameBox = AddText(grp, "Имя:", Cell("name"));
        _typeBox = AddCombo(grp, "Тип:", Ui.NpcTypes, Cell("type"));
        _radiusBox = AddNum(grp, "Радиус блуждания (0 — по умолчанию):", Cell("wander_radius"));
        _locationBox = AddText(grp, "Зона (после размещения):", Cell("location"));
        _locationBox.IsReadOnly = true;

        stack.Children.Add(grp);
        scroll.Content = stack;

        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(12, 8, 12, 12) };
        var dlgBtn = new Button { Content = "Редактор диалогов", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 6) };
        dlgBtn.Click += (s, e) => { Save(); _openDialogue(_row); };
        var dupBtn = new Button { Content = "Дублировать и разместить", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 6) };
        dupBtn.Click += (s, e) => { Save(); _duplicateNpc(_row); Close(); };
        var placeBtn = new Button { Content = "Разместить на карте", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 6) };
        placeBtn.Click += (s, e) => { Save(); _placeOnMap(_row); Close(); };
        var okBtn = new Button { Content = "Сохранить", Padding = new Thickness(10, 4, 10, 4), FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 6, 6) };
        okBtn.Click += (s, e) => Save();
        var cancelBtn = new Button { Content = "Отмена", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 6) };
        cancelBtn.Click += (s, e) => Close();
        buttons.Children.Add(dlgBtn);
        buttons.Children.Add(dupBtn);
        buttons.Children.Add(placeBtn);
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

    private void Save()
    {
        _row["id"] = _idBox.Text.Trim();
        _row["name"] = _nameBox.Text.Trim();
        _row["type"] = _typeBox.SelectedItem?.ToString() ?? "";
        int radius = Num(_radiusBox);
        if (radius < 0) radius = 0;
        _row["wander_radius"] = radius;
        _status("NPC изменён (не забудьте «Сохранить NPC и мир»)");
    }

    private string Cell(string col) => _row[col]?.ToString() ?? "";

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
        var tb = new TextBox { Text = value, Width = 140, HorizontalAlignment = HorizontalAlignment.Left };
        AddRow(g, label, tb);
        return tb;
    }

    private ComboBox AddCombo(StackPanel g, string label, string[] items, string value)
    {
        var cb = new ComboBox { ItemsSource = items, SelectedItem = value, Width = 260, HorizontalAlignment = HorizontalAlignment.Left };
        AddRow(g, label, cb);
        return cb;
    }

    private static int Num(TextBox tb) => int.TryParse(tb.Text, out int v) ? v : 0;

    private static void AddRow(StackPanel g, string label, Control control)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var lbl = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 8, 0) };
        Grid.SetColumn(lbl, 0);
        Grid.SetColumn(control, 1);
        grid.Children.Add(lbl);
        grid.Children.Add(control);
        g.Children.Add(grid);
    }
}

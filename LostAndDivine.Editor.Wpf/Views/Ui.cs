using System.Collections;
using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace LostAndDivine.Editor.Views;

/// <summary>Общие хелперы для вкладок редактора.</summary>
public static class Ui
{
    public static readonly string[] ItemTypes =
        { "weapon", "twohand", "shield", "helmet", "cloak", "chest", "legs", "boots", "glove", "belt", "necklace", "ring", "accessory", "consumable", "collectible", "trophy" };

    public static readonly string[] QuestTypes = { "kill", "collect", "talk", "travel", "use", "explore" };

    public static readonly string[] NpcTypes = { "npc", "merchant", "board", "instance_portal", "dummy", "storage" };

    /// <summary>Привязывает DataTable к гриду (автогенерация колонок).</summary>
    public static void Bind(DataGrid grid, DataTable dt)
    {
        grid.ItemsSource = dt.DefaultView;
        grid.AutoGenerateColumns = true;
    }

    public static DataView? View(DataGrid grid) => grid.ItemsSource as DataView;

    /// <summary>Фильтр по всем строковым колонкам (LIKE).</summary>
    public static void ApplyFilter(DataGrid grid, string search)
    {
        if (View(grid) is not DataView dv) return;
        search = search.Trim();
        if (string.IsNullOrWhiteSpace(search))
        {
            dv.RowFilter = "";
            return;
        }
        string escaped = search.Replace("'", "''");
        var parts = new List<string>();
        foreach (DataColumn col in dv.Table.Columns)
        {
            if (col.DataType == typeof(string))
                parts.Add($"{col.ColumnName} LIKE '%{escaped}%'");
        }
        dv.RowFilter = string.Join(" OR ", parts);
    }

    public static DataRow? SelectedRow(DataGrid grid)
    {
        if (grid.SelectedItem is not DataRowView drv) return null;
        return drv.Row;
    }

    public static void SelectLastRow(DataGrid grid, DataTable dt)
    {
        if (dt.Rows.Count == 0) return;
        grid.SelectedItem = dt.DefaultView[dt.Rows.Count - 1];
        grid.ScrollIntoView(grid.SelectedItem);
    }

    /// <summary>Добавляет строку с автозаполненным ID и выбирает её.</summary>
    public static void AddRowWithId(DataGrid grid, DataTable dt, string prefix, params object?[] values)
    {
        object?[] cells = new object?[dt.Columns.Count];
        for (int i = 0; i < cells.Length; i++) cells[i] = i < values.Length ? values[i] : "";
        cells[0] = Db.NextId(dt, prefix);
        dt.Rows.Add(cells);
        SelectLastRow(grid, dt);
    }

    public static void DeleteSelectedRow(DataGrid grid)
    {
        if (SelectedRow(grid) is DataRow row)
        {
            row.Delete();
            grid.Items.Refresh();
        }
    }

    public static void Commit(DataGrid grid)
    {
        grid.CommitEdit(DataGridEditingUnit.Row, true);
        grid.CommitEdit(DataGridEditingUnit.Cell, true);
    }

    /// <summary>Заменяет автогенерированную текстовую колонку на колонку-комбобокс.</summary>
    public static void MakeComboColumn(DataGrid grid, string columnName, IEnumerable items)
    {
        int idx = -1;
        for (int i = 0; i < grid.Columns.Count; i++)
        {
            if (grid.Columns[i].Header?.ToString() == columnName)
            {
                idx = i;
                break;
            }
        }
        if (idx < 0) return;
        var combo = new DataGridComboBoxColumn
        {
            Header = columnName,
            ItemsSource = items,
            SelectedItemBinding = new Binding(columnName)
        };
        grid.Columns.RemoveAt(idx);
        grid.Columns.Insert(idx, combo);
    }

    /// <summary>Оставляет видимыми только перечисленные колонки.</summary>
    public static void ShowOnly(DataGrid grid, params string[] names)
    {
        var keep = new HashSet<string>(names);
        foreach (var col in grid.Columns)
            col.Visibility = keep.Contains(col.Header?.ToString() ?? "") ? Visibility.Visible : Visibility.Collapsed;
    }

    public static Button ToolButton(string text) => new()
    {
        Content = text,
        Padding = new Thickness(10, 3, 10, 3),
        Margin = new Thickness(0, 0, 6, 0)
    };

    public static TextBox SearchBox(string placeholder) => new()
    {
        Width = 220,
        VerticalContentAlignment = VerticalAlignment.Center,
        ToolTip = placeholder
    };

    public static string Cell(DataRow row, string col) => row[col]?.ToString() ?? "";
}
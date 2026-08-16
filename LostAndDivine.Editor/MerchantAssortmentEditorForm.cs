using Microsoft.Data.Sqlite;
using System.Windows.Forms;

namespace LostAndDivine.Editor;

/// <summary>
/// Окно редактирования ассортимента конкретного NPC-торговца.
/// Список предметов берётся из таблицы items, а пометки — из merchant_stock (по npc_id).
/// </summary>
public class MerchantAssortmentEditorForm : Form
{
    private readonly string _dbFile;
    private readonly string _npcId;
    private readonly HashSet<string> _checked = new(StringComparer.OrdinalIgnoreCase);

    private CheckedListBox _list = null!;
    private TextBox _search = null!;
    private ComboBox _category = null!;
    private List<(string Id, string Name, string Type)> _items = new();

    public MerchantAssortmentEditorForm(string dbFile, string npcId, string npcName)
    {
        _dbFile = dbFile;
        _npcId = npcId;
        Text = $"Ассортимент: {npcName} ({npcId})";
        Width = 540;
        Height = 660;
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 10);

        var top = new Panel { Dock = DockStyle.Top, Height = 64, Padding = new Padding(6) };
        var searchRow = new Panel { Dock = DockStyle.Top, Height = 28, Padding = new Padding(6, 2, 6, 2) };
        _search = new TextBox { Dock = DockStyle.Fill };
        _search.TextChanged += (s, e) => ApplyFilter();
        searchRow.Controls.Add(_search);

        var btnRow = new Panel { Dock = DockStyle.Top, Height = 30, Padding = new Padding(6, 2, 6, 2) };
        var selAll = new Button { Text = "Выбрать все", Dock = DockStyle.Left, Width = 110 };
        selAll.Click += (s, e) => { for (int i = 0; i < _list.Items.Count; i++) _list.SetItemChecked(i, true); };
        var selNone = new Button { Text = "Снять все", Dock = DockStyle.Left, Width = 110 };
        selNone.Click += (s, e) => { for (int i = 0; i < _list.Items.Count; i++) _list.SetItemChecked(i, false); };
        _category = new ComboBox { Dock = DockStyle.Left, Width = 130, DropDownStyle = ComboBoxStyle.DropDownList };
        _category.Items.AddRange(new object[] { "все", "Оружие", "Доспехи", "Расходники", "Другое" });
        _category.SelectedIndex = 0;
        _category.SelectedIndexChanged += (s, e) => ApplyFilter();
        btnRow.Controls.Add(_category);
        btnRow.Controls.Add(selNone);
        btnRow.Controls.Add(selAll);
        top.Controls.Add(btnRow);
        top.Controls.Add(searchRow);

        _list = new CheckedListBox
        {
            Dock = DockStyle.Fill,
            CheckOnClick = true,
            Font = new Font("Segoe UI", 10),
            BorderStyle = BorderStyle.FixedSingle
        };
        _list.ItemCheck += (s, e) =>
        {
            var id = ItemId(e.Index);
            if (id == null) return;
            if (e.NewValue == CheckState.Checked) _checked.Add(id);
            else _checked.Remove(id);
        };

        var save = new Button { Text = "Сохранить ассортимент", Dock = DockStyle.Bottom, Height = 34 };
        save.Click += (s, e) => Save();

        Controls.Add(_list);
        Controls.Add(top);
        Controls.Add(save);

        LoadStock();
        LoadItems();
    }

    private void LoadStock()
    {
        _checked.Clear();
        using var conn = new SqliteConnection($"Data Source={_dbFile}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT item_id FROM merchant_stock WHERE npc_id = $npc";
        cmd.Parameters.AddWithValue("$npc", _npcId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) _checked.Add(reader.GetString(0));
    }

    private void LoadItems()
    {
        _items = new List<(string, string, string)>();
        using var conn = new SqliteConnection($"Data Source={_dbFile}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, type FROM items WHERE type <> 'collectible' ORDER BY id";
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) _items.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        ApplyFilter();
    }

    private string? ItemId(int index)
    {
        var text = _list.Items[index]?.ToString() ?? "";
        int sep = text.IndexOf("  —  ");
        return sep > 0 ? text.Substring(0, sep).Trim() : null;
    }

    private void ApplyFilter()
    {
        string search = _search.Text;
        string cat = _category.SelectedItem?.ToString() ?? "все";
        _list.Items.Clear();
        foreach (var (id, name, type) in _items)
        {
            bool matchSearch = string.IsNullOrWhiteSpace(search) ||
                $"{id} {name} {type}".Contains(search, StringComparison.OrdinalIgnoreCase);
            bool matchCat = cat == "все" || type.Contains(cat, StringComparison.OrdinalIgnoreCase);
            if (matchSearch && matchCat)
                _list.Items.Add($"{id}  —  {name}  [{type}]", _checked.Contains(id));
        }
    }

    private void Save()
    {
        try
        {
            using var conn = new SqliteConnection($"Data Source={_dbFile}");
            conn.Open();
            using var transaction = conn.BeginTransaction();
            using (var del = conn.CreateCommand())
            {
                del.CommandText = "DELETE FROM merchant_stock WHERE npc_id = $npc";
                del.Parameters.AddWithValue("$npc", _npcId);
                del.ExecuteNonQuery();
            }
            foreach (var itemId in _checked)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT OR IGNORE INTO merchant_stock (npc_id, item_id) VALUES ($npc, $item)";
                cmd.Parameters.AddWithValue("$npc", _npcId);
                cmd.Parameters.AddWithValue("$item", itemId);
                cmd.ExecuteNonQuery();
            }
            transaction.Commit();
            MessageBox.Show($"Ассортимент сохранён: {_checked.Count} предметов", "Готово",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Ошибка сохранения: " + ex.Message, "Ошибка",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

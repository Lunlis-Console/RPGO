using System.Data;
using System.Windows;
using System.Windows.Controls;
using LostAndDivine.Shared.Models;
using Microsoft.Data.Sqlite;

namespace LostAndDivine.Editor.Views;

public partial class AccountsTabView : UserControl
{
    private MainWindow _win = null!;
    private Db _db = null!;
    private DataTable _dt = new();

    public AccountsTabView()
    {
        InitializeComponent();
        Search.TextChanged += (s, e) => ApplyAccountsFilter();
        AddBtn.Click += (s, e) => AddAccount();
        DeleteBtn.Click += (s, e) => Ui.DeleteSelectedRow(Grid);
        BanBtn.Click += (s, e) => ToggleBan();
        AdminBtn.Click += (s, e) => ToggleAdmin();
        ResetPwdBtn.Click += (s, e) => ResetPassword();
        GiveItemBtn.Click += (s, e) => GiveItem();
        SaveBtn.Click += (s, e) => SaveAccounts();
        Grid.SelectionChanged += (s, e) => LoadPlayerInventory();
    }

    public void Init(MainWindow win)
    {
        _win = win;
        _db = win.Db;
        LoadAccounts();
    }

    private void LoadAccounts()
    {
        _dt = BuildAccountsTable();
        using var conn = _db.OpenGame();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM accounts ORDER BY login";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var row = _dt.NewRow();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                string col = reader.GetName(i);
                if (col == "is_admin" || col == "is_banned")
                    row[col] = reader.IsDBNull(i) ? false : reader.GetInt32(i) != 0;
                else
                    row[col] = reader.IsDBNull(i) ? (object)DBNull.Value : reader.GetValue(i);
            }
            _dt.Rows.Add(row);
        }
        Ui.Bind(Grid, _dt);
        HideColumns();
        Grid.SelectedIndex = _dt.Rows.Count > 0 ? 0 : -1;
        ApplyAccountsFilter();
        _win.Status($"Аккаунты: {_dt.Rows.Count}");
    }

    private static DataTable BuildAccountsTable()
    {
        var dt = new DataTable();
        dt.Columns.Add("login", typeof(string));
        dt.Columns.Add("password_hash", typeof(string));
        dt.Columns.Add("player_name", typeof(string));
        dt.Columns.Add("level", typeof(string));
        dt.Columns.Add("experience", typeof(string));
        dt.Columns.Add("health", typeof(string));
        dt.Columns.Add("max_health", typeof(string));
        dt.Columns.Add("phys_attack", typeof(string));
        dt.Columns.Add("phys_defense", typeof(string));
        dt.Columns.Add("gold", typeof(string));
        dt.Columns.Add("created_at", typeof(string));
        dt.Columns.Add("last_login", typeof(string));
        dt.Columns.Add("weapon_id", typeof(string));
        dt.Columns.Add("armor_id", typeof(string));
        dt.Columns.Add("accessory_id", typeof(string));
        dt.Columns.Add("strength", typeof(string));
        dt.Columns.Add("endurance", typeof(string));
        dt.Columns.Add("agility", typeof(string));
        dt.Columns.Add("cunning", typeof(string));
        dt.Columns.Add("intellect", typeof(string));
        dt.Columns.Add("wisdom", typeof(string));
        dt.Columns.Add("attribute_points", typeof(string));
        dt.Columns.Add("speed", typeof(string));
        dt.Columns.Add("pos_x", typeof(string));
        dt.Columns.Add("pos_y", typeof(string));
        dt.Columns.Add("hotbar_slots", typeof(string));
        dt.Columns.Add("is_admin", typeof(bool));
        dt.Columns.Add("is_banned", typeof(bool));
        dt.Columns.Add("ban_reason", typeof(string));
        dt.Columns.Add("skill_points", typeof(string));
        dt.Columns.Add("learned_skills", typeof(string));
        dt.Columns.Add("current_zone", typeof(string));
        dt.Columns.Add("mana", typeof(string));
        dt.Columns.Add("skill_ranks", typeof(string));
        return dt;
    }

    private void HideColumns()
    {
        var hidden = new[]
        {
            "password_hash", "created_at", "last_login",
            "weapon_id", "armor_id", "accessory_id",
            "hotbar_slots", "learned_skills", "skill_ranks",
            "pos_x", "pos_y"
        };
        foreach (var name in hidden)
        {
            var col = FindColumn(name);
            if (col != null) col.Visibility = Visibility.Collapsed;
        }
        foreach (var col in Grid.Columns)
        {
            switch (col.Header?.ToString())
            {
                case "is_admin": col.Header = "Админ"; break;
                case "is_banned": col.Header = "Бан"; break;
            }
        }
    }

    private DataGridColumn? FindColumn(string name)
    {
        foreach (var col in Grid.Columns)
            if (col.Header?.ToString() == name) return col;
        return null;
    }

    private void ApplyAccountsFilter()
    {
        if (Grid.ItemsSource is not DataView dv) return;
        string search = Search.Text.Trim().Replace("'", "''");
        dv.RowFilter = string.IsNullOrWhiteSpace(search)
            ? ""
            : $"login LIKE '%{search}%' OR player_name LIKE '%{search}%'";
    }

    private void AddAccount()
    {
        object?[] cells = new object?[_dt.Columns.Count];
        for (int i = 0; i < cells.Length; i++) cells[i] = "";
        _dt.Rows.Add(cells);
        Ui.SelectLastRow(Grid, _dt);
    }

    private void LoadPlayerInventory()
    {
        if (Ui.SelectedRow(Grid) is not DataRow row)
        {
            InventoryGrid.ItemsSource = null;
            SelectedPlayerLabel.Text = "Инвентарь: выберите аккаунт";
            return;
        }
        string login = row["login"]?.ToString() ?? "";
        string playerName = row["player_name"]?.ToString() ?? "";
        SelectedPlayerLabel.Text = $"Инвентарь: {login} ({playerName})";

        var dt = new DataTable();
        using var conn = _db.OpenGame();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT i.id, i.item_id, i.name, i.type, i.quantity, i.value,
                       i.damage_min, i.damage_max, i.defense
                FROM inventory i
                WHERE i.player_name = $p
                ORDER BY i.id";
        cmd.Parameters.AddWithValue("$p", playerName);
        using var reader = cmd.ExecuteReader();
        dt.Load(reader);
        InventoryGrid.ItemsSource = dt.DefaultView;
    }

    private void ToggleBan()
    {
        if (Ui.SelectedRow(Grid) is not DataRow row) return;
        bool current = Db.IsChecked(row["is_banned"]);
        row["is_banned"] = !current;
        row["ban_reason"] = !current ? "Нарушение правил" : "";
        _win.Status(current ? "Аккаунт разбанен" : "Аккаунт забанен");
    }

    private void ToggleAdmin()
    {
        if (Ui.SelectedRow(Grid) is not DataRow row) return;
        bool current = Db.IsChecked(row["is_admin"]);
        row["is_admin"] = !current;
        _win.Status(current ? "Админ права сняты" : "Админ права выданы");
    }

    private void ResetPassword()
    {
        if (Ui.SelectedRow(Grid) is not DataRow row) return;
        string login = row["login"]?.ToString() ?? "";
        var result = MessageBox.Show(Window.GetWindow(this),
            $"Сбросить пароль для {login} на '123'?", "Сброс пароля",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;
        using var conn = _db.OpenGame();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE accounts SET password_hash = $p WHERE login = $l";
        cmd.Parameters.AddWithValue("$p", Db.HashPassword("123"));
        cmd.Parameters.AddWithValue("$l", login);
        cmd.ExecuteNonQuery();
        _win.Status($"Пароль для {login} сброшен на '123'");
    }

    private void GiveItem()
    {
        if (Ui.SelectedRow(Grid) is not DataRow row) return;
        string login = row["login"]?.ToString() ?? "";
        string playerName = row["player_name"]?.ToString() ?? "";

        var items = new List<(string Id, string Name, string Type)>();
        using (var conn = _db.OpenGame())
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, name, type FROM items ORDER BY id";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                items.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }
        if (items.Count == 0)
        {
            MessageBox.Show(Window.GetWindow(this), "Нет предметов в базе", "Ошибка",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var picker = new ItemPickerWindow(items);
        picker.Owner = Window.GetWindow(this);
        if (picker.ShowDialog() != true) return;

        using var insConn = _db.OpenGame();
        using var insCmd = insConn.CreateCommand();
        insCmd.CommandText = "INSERT INTO inventory (player_name, item_id, name, type, value, quantity) VALUES ($p, $id, $n, $t, $v, $q)";
        insCmd.Parameters.AddWithValue("$p", playerName);
        insCmd.Parameters.AddWithValue("$id", picker.SelectedId);
        insCmd.Parameters.AddWithValue("$n", picker.SelectedName);
        insCmd.Parameters.AddWithValue("$t", picker.SelectedType);
        insCmd.Parameters.AddWithValue("$v", 0);
        insCmd.Parameters.AddWithValue("$q", picker.Quantity);
        insCmd.ExecuteNonQuery();
        _win.Status($"Выдано {picker.Quantity}x {picker.SelectedName} игроку {login}");
        LoadPlayerInventory();
    }

    private void SaveAccounts()
    {
        try
        {
            Ui.Commit(Grid);
            var hashes = new Dictionary<string, string>();
            using (var readConn = _db.OpenGame())
            {
                using var cmd = readConn.CreateCommand();
                cmd.CommandText = "SELECT login, password_hash FROM accounts";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    if (!reader.IsDBNull(1)) hashes[reader.GetString(0)] = reader.GetString(1);
                }
            }

            using var conn = _db.OpenGame();
            using var transaction = conn.BeginTransaction();
            var logins = new List<string>();
            foreach (DataRow dr in _dt.Rows)
            {
                if (dr.RowState == DataRowState.Deleted) continue;
                var login = dr["login"]?.ToString();
                if (!string.IsNullOrWhiteSpace(login)) logins.Add(login!);
            }
            Db.DeleteMissingRows(conn, transaction, "accounts", "login", logins);
            foreach (DataRow dr in _dt.Rows)
            {
                if (dr.RowState == DataRowState.Deleted) continue;
                if (string.IsNullOrWhiteSpace(dr["login"]?.ToString())) continue;
                string login = dr["login"].ToString()!;
                string hash = !string.IsNullOrWhiteSpace(dr["password_hash"]?.ToString())
                    ? dr["password_hash"].ToString()!
                    : hashes.TryGetValue(login, out var h) ? h : Db.HashPassword("123");

                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"INSERT OR REPLACE INTO accounts (login, password_hash, player_name, level, experience,
                        health, max_health, phys_attack, phys_defense, gold, created_at, last_login,
                        weapon_id, armor_id, accessory_id,
                        strength, endurance, agility, cunning, intellect, wisdom,
                        attribute_points, speed, pos_x, pos_y,
                        hotbar_slots, is_admin, is_banned, ban_reason, skill_points, learned_skills, current_zone,
                        mana, skill_ranks)
                    VALUES ($l, $ph, $pn, $lv, $exp,
                        $hp, $mhp, $pa, $pd, $g, $ca, $ll,
                        $wi, $ai, $aci,
                        $str, $end, $agi, $cun, $int, $wis,
                        $ap, $spd, $px, $py,
                        $hs, $adm, $ban, $br, $skp, $ls, $cz,
                        $mana, $sr)";
                cmd.Parameters.AddWithValue("$l", login);
                cmd.Parameters.AddWithValue("$ph", hash);
                cmd.Parameters.AddWithValue("$pn", dr["player_name"] ?? "");
                cmd.Parameters.AddWithValue("$lv", Db.ToInt(dr["level"]));
                cmd.Parameters.AddWithValue("$exp", Db.ToInt(dr["experience"]));
                cmd.Parameters.AddWithValue("$hp", Db.ToInt(dr["health"]));
                cmd.Parameters.AddWithValue("$mhp", Db.ToInt(dr["max_health"]));
                cmd.Parameters.AddWithValue("$pa", Db.ToInt(dr["phys_attack"]));
                cmd.Parameters.AddWithValue("$pd", Db.ToInt(dr["phys_defense"]));
                cmd.Parameters.AddWithValue("$g", Db.ToInt(dr["gold"]));
                cmd.Parameters.AddWithValue("$ca", DateTime.UtcNow.ToString("O"));
                cmd.Parameters.AddWithValue("$ll", DateTime.UtcNow.ToString("O"));
                cmd.Parameters.AddWithValue("$wi", dr["weapon_id"] ?? "");
                cmd.Parameters.AddWithValue("$ai", dr["armor_id"] ?? "");
                cmd.Parameters.AddWithValue("$aci", dr["accessory_id"] ?? "");
                cmd.Parameters.AddWithValue("$str", Db.ToInt(dr["strength"]));
                cmd.Parameters.AddWithValue("$end", Db.ToInt(dr["endurance"]));
                cmd.Parameters.AddWithValue("$agi", Db.ToInt(dr["agility"]));
                cmd.Parameters.AddWithValue("$cun", Db.ToInt(dr["cunning"]));
                cmd.Parameters.AddWithValue("$int", Db.ToInt(dr["intellect"]));
                cmd.Parameters.AddWithValue("$wis", Db.ToInt(dr["wisdom"]));
                cmd.Parameters.AddWithValue("$ap", Db.ToInt(dr["attribute_points"]));
                cmd.Parameters.AddWithValue("$spd", Db.ToInt(dr["speed"]));
                cmd.Parameters.AddWithValue("$px", Db.ToInt(dr["pos_x"]));
                cmd.Parameters.AddWithValue("$py", Db.ToInt(dr["pos_y"]));
                cmd.Parameters.AddWithValue("$hs", dr["hotbar_slots"] ?? "");
                cmd.Parameters.AddWithValue("$adm", Db.IsChecked(dr["is_admin"]) ? 1 : 0);
                cmd.Parameters.AddWithValue("$ban", Db.IsChecked(dr["is_banned"]) ? 1 : 0);
                cmd.Parameters.AddWithValue("$br", dr["ban_reason"] ?? "");
                cmd.Parameters.AddWithValue("$skp", Db.ToInt(dr["skill_points"]));
                cmd.Parameters.AddWithValue("$ls", dr["learned_skills"] ?? "[]");
                cmd.Parameters.AddWithValue("$cz", dr["current_zone"] ?? BalanceStatic.MainZoneId);
                cmd.Parameters.AddWithValue("$mana", Db.ToInt(dr["mana"]));
                cmd.Parameters.AddWithValue("$sr", dr["skill_ranks"] ?? "{}");
                cmd.ExecuteNonQuery();
            }
            transaction.Commit();
            LoadAccounts();
            _win.Status("Аккаунты сохранены");
        }
        catch (Exception ex) { _win.Status("Ошибка (аккаунты): " + ex.Message); }
    }

    private sealed class ItemPickerWindow : Window
    {
        public string SelectedId { get; private set; } = "";
        public string SelectedName { get; private set; } = "";
        public string SelectedType { get; private set; } = "";
        public int Quantity { get; private set; } = 1;

        private readonly List<(string Id, string Name, string Type)> _items;
        private readonly ListBox _list = new();
        private readonly TextBox _search = new();
        private readonly TextBox _qty = new() { Text = "1", Width = 60, VerticalContentAlignment = VerticalAlignment.Center };

        public ItemPickerWindow(List<(string Id, string Name, string Type)> items)
        {
            _items = items;
            Title = "Выдать предмет";
            Width = 520;
            Height = 520;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var grid = new Grid { Margin = new Thickness(8) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _search.Margin = new Thickness(0, 0, 0, 6);
            _search.TextChanged += (s, e) => ApplyFilter();
            System.Windows.Controls.Grid.SetRow(_search, 0);

            _list.MouseDoubleClick += (s, e) => { if (_list.SelectedItem != null) Ok(); };
            System.Windows.Controls.Grid.SetRow(_list, 1);

            var bottom = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            var qtyLabel = new TextBlock { Text = "Количество:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            DockPanel.SetDock(qtyLabel, Dock.Left);
            var okBtn = new Button { Content = "Выдать", Width = 90, Height = 30, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Right };
            okBtn.Click += (s, e) => Ok();
            var cancelBtn = new Button { Content = "Отмена", Width = 90, Height = 30, Margin = new Thickness(8, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
            cancelBtn.Click += (s, e) => Close();
            bottom.Children.Add(okBtn);
            bottom.Children.Add(cancelBtn);
            bottom.Children.Add(_qty);
            bottom.Children.Add(qtyLabel);
            System.Windows.Controls.Grid.SetRow(bottom, 2);

            grid.Children.Add(_search);
            grid.Children.Add(_list);
            grid.Children.Add(bottom);
            Content = grid;
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            string f = _search.Text.Trim().ToLowerInvariant();
            var filtered = string.IsNullOrWhiteSpace(f)
                ? _items
                : _items.Where(i => i.Name.ToLowerInvariant().Contains(f) || i.Id.ToLowerInvariant().Contains(f)).ToList();
            _list.ItemsSource = filtered.Select(i => $"{i.Id}  —  {i.Name}  [{i.Type}]").ToList();
        }

        private void Ok()
        {
            if (_list.SelectedItem is not string line) return;
            int sep = line.IndexOf("  —  ");
            if (sep < 0) return;
            string id = line[..sep].Trim();
            var item = _items.First(i => i.Id == id);
            if (!int.TryParse(_qty.Text, out int q) || q < 1) q = 1;
            SelectedId = item.Id;
            SelectedName = item.Name;
            SelectedType = item.Type;
            Quantity = q;
            DialogResult = true;
        }
    }
}
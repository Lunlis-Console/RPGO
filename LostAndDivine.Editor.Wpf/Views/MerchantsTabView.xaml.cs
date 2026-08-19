using System.Data;
using System.Windows;
using System.Windows.Controls;

namespace LostAndDivine.Editor.Views;

public partial class MerchantsTabView : UserControl
{
    private MainWindow _win = null!;
    private Db _db = null!;
    private DataTable _dt = new();

    public MerchantsTabView()
    {
        InitializeComponent();
        AssortBtn.Click += (s, e) => OpenAssortment();
        RefreshBtn.Click += (s, e) => LoadMerchants();
        Grid.MouseDoubleClick += (s, e) => { if (e.OriginalSource is TextBlock or Border) OpenAssortment(); };
    }

    public void Init(MainWindow win)
    {
        _win = win;
        _db = win.Db;
        LoadMerchants();
    }

    private void LoadMerchants()
    {
        try
        {
            _dt = new DataTable();
            _dt.Columns.Add("id", typeof(string));
            _dt.Columns.Add("name", typeof(string));
            _dt.Columns.Add("location", typeof(string));
            using var conn = _db.OpenContent();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, name FROM npcs WHERE type = 'merchant' ORDER BY id";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                string name = reader.GetString(1);
                _dt.Rows.Add(reader.GetString(0), name, _db.LocationByName(name));
            }
            Grid.ItemsSource = _dt.DefaultView;
            _win.Status($"Торговцы: {_dt.Rows.Count}");
        }
        catch (Exception ex) { _win.Status("Ошибка (торговцы): " + ex.Message); }
    }

    private void OpenAssortment()
    {
        if (Grid.SelectedItem is not DataRowView drv)
        {
            _win.Status("Выберите торговца в таблице");
            return;
        }
        string npcId = drv["id"]?.ToString() ?? "";
        string npcName = drv["name"]?.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(npcId)) return;
        var dlg = new MerchantAssortmentWindow(_db, npcId, npcName);
        dlg.Owner = Window.GetWindow(this);
        dlg.ShowDialog();
        _win.Status("Ассортимент обновлён");
    }
}
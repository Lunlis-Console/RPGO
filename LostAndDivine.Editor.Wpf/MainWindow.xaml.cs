using System.IO;
using System.Windows;
using System.Windows.Threading;
using LostAndDivine.Editor.Views;
using Microsoft.Win32;

namespace LostAndDivine.Editor;

public partial class MainWindow : Window
{
    public Db Db { get; }

    public MainWindow()
    {
        InitializeComponent();

        string? dbFile = FindDatabase() ?? PickDatabase();
        if (dbFile == null)
        {
            MessageBox.Show("Не выбран файл базы данных.", "Редактор LostAndDivine",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            Application.Current.Shutdown();
            return;
        }

        Db = new Db(dbFile);
        Title = "Редактор LostAndDivine — " + Path.GetFileName(dbFile);
        Db.InitAndLoadAll();

        ItemsTab.Init(this);
        MonstersTab.Init(this);
        QuestsTab.Init(this);
        NpcsTab.Init(this);
        MerchantsTab.Init(this);
        AnimationsTab.Init(this);
        AccountsTab.Init(this);

        Status("Загружено: предметы, монстры, квесты, NPC, торговцы, анимации, аккаунты");
    }

    public void Status(string text)
        => StatusText.Text = $"[{DateTime.Now:HH:mm:ss}] {text}";

    private void PublishButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            PublishButton.IsEnabled = false;
            Db.PublishToLive();
            Status("Контент опубликован в живой content.db (старый бэкапирован).");
            MessageBox.Show(
                "Контент из content.editor.db опубликован в живой content.db сервера.\n" +
                "Старый content.db предварительно сохранён в бэкап (content.db.publishbak_*).\n" +
                "Чтобы сервер увидел изменения, перезапустите его или выполните перезагрузку контента.",
                "Публикация контента", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Status("Ошибка публикации: " + ex.Message);
            MessageBox.Show("Не удалось опубликовать контент:\n" + ex.Message,
                "Ошибка публикации", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            PublishButton.IsEnabled = true;
        }
    }

    public static string? PickDatabase()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Выберите файл базы данных game.db",
            Filter = "SQLite DB (*.db)|*.db|Все файлы (*.*)|*.*"
        };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    /// <summary>Находит game.db рядом с серверным проектом (та же база, что и у сервера).</summary>
    public static string? FindDatabase()
    {
        string? baseDir = AppContext.BaseDirectory;
        for (int i = 0; i < 8; i++)
        {
            baseDir = Path.GetDirectoryName(baseDir);
            if (baseDir == null) break;
            if (File.Exists(Path.Combine(baseDir, "LostAndDivine.Server.csproj")))
            {
                var serverDb = Path.Combine(baseDir, "game.db");
                if (File.Exists(serverDb)) return Path.GetFullPath(serverDb);
            }
        }

        baseDir = AppContext.BaseDirectory;
        for (int i = 0; i < 8; i++)
        {
            if (File.Exists(Path.Combine(baseDir, "game.db")))
                return Path.GetFullPath(Path.Combine(baseDir, "game.db"));
            baseDir = Path.GetDirectoryName(baseDir);
            if (baseDir == null) break;
        }
        return null;
    }
}
using System.Windows.Forms;

namespace RPGGame.Editor;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        string? dbFile = FindDatabase();
        if (dbFile == null)
        {
            using var dlg = new OpenFileDialog
            {
                Title = "Выберите файл базы данных game.db",
                Filter = "SQLite DB (*.db)|*.db|Все файлы (*.*)|*.*"
            };
            if (dlg.ShowDialog() == DialogResult.OK)
                dbFile = dlg.FileName;
            else
                return;
        }

        Application.Run(new MainForm(dbFile));
    }

    // Используем game.db рядом с RPGO.Server.csproj (та же база что и сервер):
    private static string? FindDatabase()
    {
        // Сначала ищем game.db рядом с серверным проектом — единая база для обоих
        string? baseDir = AppContext.BaseDirectory;
        for (int i = 0; i < 8; i++)
        {
            baseDir = Path.GetDirectoryName(baseDir);
            if (baseDir == null) break;
            if (File.Exists(Path.Combine(baseDir, "RPGO.Server.csproj")))
            {
                var serverDb = Path.Combine(baseDir, "game.db");
                if (File.Exists(serverDb)) return Path.GetFullPath(serverDb);
            }
        }

        // Fallback: первый попавшийся game.db
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

using LostAndDivine.Shared;
using LostAndDivine.Shared.Network;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text.Json;
using System.Windows;

namespace LostAndDivine.Launcher;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<ChangelogItem> _changelog = new();
    private string _serverIp = "127.0.0.1";
    private bool _busy;

    public MainWindow()
    {
        InitializeComponent();
        ChangelogList.ItemsSource = _changelog;

        _serverIp = ReadServerIp();
        ServerIpBox.Text = _serverIp;
        VersionLabel.Text = $"локальная v{GameUpdater.LocalVersion}";

        CheckButton.Click += async (_, _) => await CheckUpdatesAsync();
        PlayButton.Click += (_, _) => Play();
        SaveIpButton.Click += (_, _) => SaveServerIp(ServerIpBox.Text.Trim());

        Loaded += async (_, _) => await StartupAsync();
    }

    private async Task StartupAsync()
    {
        await LoadChangelogAsync();
        // Автоматическая проверка и применение обновлений при запуске (как в серьёзных играх).
        await CheckUpdatesAsync();
    }

    private async Task LoadChangelogAsync()
    {
        try
        {
            var cl = await GameUpdater.FetchChangelogAsync(_serverIp);
            _changelog.Clear();
            if (cl?.Entries != null)
            {
                foreach (var e in cl.Entries.OrderByDescending(x => x.Version))
                    _changelog.Add(new ChangelogItem
                    {
                        Title = $"v{e.Version}" + (string.IsNullOrWhiteSpace(e.Date) ? "" : $"  ({e.Date})"),
                        Lines = e.Items ?? new()
                    });
            }
            if (_changelog.Count == 0)
                _changelog.Add(new ChangelogItem { Title = "История пуста", Lines = new() });
        }
        catch
        {
            _changelog.Clear();
            _changelog.Add(new ChangelogItem { Title = "Не удалось загрузить историю", Lines = new() });
        }
    }

    private async Task CheckUpdatesAsync()
    {
        if (_busy) return;
        _busy = true;
        CheckButton.IsEnabled = false;
        PlayButton.IsEnabled = false;
        try
        {
            var progress = new Progress<string>(m => StatusLabel.Text = m);
            var result = await GameUpdater.CheckAndApplyAsync(_serverIp, progress);
            StatusLabel.Text = result.Message;
            VersionLabel.Text = $"локальная v{GameUpdater.LocalVersion}";
            if (result.RestartRequired)
            {
                // Обновление записано в staging — применяем и перезапускаем лаунчер.
                GameUpdater.RestartToApply();
                Application.Current.Shutdown();
                return;
            }
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Ошибка: {ex.Message}";
        }
        finally
        {
            _busy = false;
            CheckButton.IsEnabled = true;
            PlayButton.IsEnabled = true;
        }
    }

    private void Play()
    {
        string gameExe = Path.Combine(AppContext.BaseDirectory, "LostAndDivine.ClientMonoGame.exe");
        if (!File.Exists(gameExe))
        {
            StatusLabel.Text = "Игра не найдена рядом с лаунчером (LostAndDivine.ClientMonoGame.exe).";
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(gameExe)
            {
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = true
            });
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Не удалось запустить игру: {ex.Message}";
        }
    }

    private static string ReadServerIp()
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "settings.json");
            if (File.Exists(path))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (doc.RootElement.TryGetProperty("ServerIp", out var v))
                    return v.GetString() ?? "127.0.0.1";
            }
        }
        catch { }
        return "127.0.0.1";
    }

    private void SaveServerIp(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return;
        _serverIp = ip;
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "settings.json");
            var obj = File.Exists(path)
                ? JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(path))!
                : new Dictionary<string, JsonElement>();
            obj["ServerIp"] = JsonSerializer.SerializeToElement(ip);
            File.WriteAllText(path, JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true }));
            StatusLabel.Text = $"Сервер сохранён: {ip}";
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Не удалось сохранить сервер: {ex.Message}";
        }
    }
}

public class ChangelogItem
{
    public string Title { get; set; } = "";
    public List<string> Lines { get; set; } = new();
}

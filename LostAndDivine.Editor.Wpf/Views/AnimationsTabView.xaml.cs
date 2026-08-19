using System.Collections.ObjectModel;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;

namespace LostAndDivine.Editor.Views;

public partial class AnimationsTabView : UserControl
{
    private MainWindow _win = null!;
    private Db _db = null!;
    private readonly ObservableCollection<AnimEntry> _entries = new();
    private readonly Dictionary<string, string> _animSrcPaths = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly System.Diagnostics.Stopwatch _stopwatch = new();
    private System.Drawing.Image? _previewImage;

    public AnimationsTabView()
    {
        InitializeComponent();
        AddBtn.Click += (s, e) => AddAnimation();
        DeleteBtn.Click += (s, e) => DeleteAnimation();
        SaveBtn.Click += (s, e) => SaveAnimations();
        Grid.SelectedCellsChanged += (s, e) => UpdateAnimPreview();
        Grid.CellEditEnding += (s, e) => UpdateAnimPreview();
        Grid.ItemsSource = _entries;
        _timer.Tick += (s, e) => DrawAnimPreviewFrame();
        Unloaded += (s, e) => _timer.Stop();
    }

    public void Init(MainWindow win)
    {
        _win = win;
        _db = win.Db;
        LoadAnimationsGrid();
        _stopwatch.Restart();
        _timer.Start();
    }

    private void LoadAnimationsGrid()
    {
        _entries.Clear();
        _animSrcPaths.Clear();
        string jsonPath = Path.Combine(_db.ClientBinContent(), "animations.json");
        if (!File.Exists(jsonPath)) return;
        try
        {
            var parsed = JsonSerializer.Deserialize<List<AnimEntry>>(File.ReadAllText(jsonPath));
            if (parsed == null) return;
            foreach (var e in parsed)
            {
                _entries.Add(new AnimEntry { Key = e.Key, Sheet = e.Sheet, Cols = e.Cols, Rows = e.Rows, Fps = e.Fps });
                string? src = ResolveSheetPath(e.Sheet);
                if (src != null) _animSrcPaths[e.Sheet] = src;
            }
        }
        catch (Exception ex) { _win.Status("Ошибка чтения animations.json: " + ex.Message); }
    }

    private void AddAnimation()
    {
        var dlg = new OpenFileDialog { Filter = "PNG спрайт-лист|*.png", Title = "Выберите PNG спрайт-лист" };
        if (dlg.ShowDialog() != true) return;
        string path = dlg.FileName;
        string fileName = Path.GetFileName(path);
        string key = Path.GetFileNameWithoutExtension(path);
        int cols = 4, rows = 1, fps = 8;
        try
        {
            using var img = System.Drawing.Image.FromFile(path);
            cols = Math.Max(1, (int)Math.Round((double)img.Width / Math.Max(1, img.Height)));
        }
        catch { }

        var existing = _entries.FirstOrDefault(e => e.Key == key);
        if (existing != null)
        {
            existing.Sheet = fileName;
            existing.Cols = cols;
            existing.Rows = rows;
            existing.Fps = fps;
            _animSrcPaths[fileName] = path;
            UpdateAnimPreview();
            _win.Status($"Анимация '{key}' обновлена");
            return;
        }
        _entries.Add(new AnimEntry { Key = key, Sheet = fileName, Cols = cols, Rows = rows, Fps = fps });
        _animSrcPaths[fileName] = path;
        Grid.SelectedItem = _entries[^1];
        UpdateAnimPreview();
        _win.Status($"Анимация '{key}' добавлена");
    }

    private void DeleteAnimation()
    {
        if (Grid.SelectedItem is not AnimEntry entry) return;
        _entries.Remove(entry);
        if (entry.Sheet != null) _animSrcPaths.Remove(entry.Sheet);
        UpdateAnimPreview();
    }

    private string? ResolveSheetPath(string sheet)
    {
        if (_animSrcPaths.TryGetValue(sheet, out var src) && File.Exists(src)) return src;
        string binPath = Path.Combine(_db.ClientBinContent(), "Animations", sheet);
        if (File.Exists(binPath)) return binPath;
        string srcPath = Path.Combine(_db.ClientSrcContent(), "Animations", sheet);
        if (File.Exists(srcPath)) return srcPath;
        return null;
    }

    private void UpdateAnimPreview()
    {
        _previewImage?.Dispose();
        _previewImage = null;
        Preview.Source = null;
        if (Grid.SelectedItem is not AnimEntry entry) return;
        if (string.IsNullOrWhiteSpace(entry.Sheet)) return;
        string? path = ResolveSheetPath(entry.Sheet);
        if (path == null || !File.Exists(path)) return;
        try { _previewImage = System.Drawing.Image.FromFile(path); } catch { _previewImage = null; }
        _stopwatch.Restart();
    }

    private void DrawAnimPreviewFrame()
    {
        if (_previewImage == null || Grid.SelectedItem is not AnimEntry entry) return;
        int cols = Math.Max(1, entry.Cols);
        int rows = Math.Max(1, entry.Rows);
        int fps = Math.Max(1, entry.Fps);
        int fw = _previewImage.Width / cols;
        int fh = _previewImage.Height / rows;
        int total = cols * rows;
        int frame = (int)(_stopwatch.Elapsed.TotalSeconds * fps) % total;
        int c = frame % cols;
        int r = frame / cols;
        var src = new System.Drawing.Rectangle(c * fw, r * fh, fw, fh);
        int targetW = Math.Max(1, Math.Min(200, fw * 3));
        int targetH = Math.Max(1, (int)((double)targetW / fw * fh));
        using var bmp = new System.Drawing.Bitmap(targetW, targetH);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(_previewImage, new System.Drawing.Rectangle(0, 0, targetW, targetH), src, System.Drawing.GraphicsUnit.Pixel);
        }
        Preview.Source = Gdi.ToBitmapSource(bmp);
    }

    private void SaveAnimations()
    {
        try
        {
            Grid.CommitEdit(DataGridEditingUnit.Row, true);
            Grid.CommitEdit(DataGridEditingUnit.Cell, true);
            var entries = _entries
                .Where(e => !string.IsNullOrWhiteSpace(e.Key) && !string.IsNullOrWhiteSpace(e.Sheet))
                .Select(e => new AnimEntry
                {
                    Key = e.Key.Trim(),
                    Sheet = e.Sheet.Trim(),
                    Cols = Math.Max(1, e.Cols),
                    Rows = Math.Max(1, e.Rows),
                    Fps = Math.Max(1, e.Fps)
                })
                .ToList();
            string json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
            foreach (var content in new[] { _db.ClientBinContent(), _db.ClientSrcContent() })
            {
                Directory.CreateDirectory(content);
                Directory.CreateDirectory(Path.Combine(content, "Animations"));
                File.WriteAllText(Path.Combine(content, "animations.json"), json);
                foreach (var e in entries)
                {
                    if (_animSrcPaths.TryGetValue(e.Sheet, out var src) && File.Exists(src))
                        File.Copy(src, Path.Combine(content, "Animations", e.Sheet), true);
                }
            }
            _win.Status($"Анимации сохранены: {entries.Count}");
        }
        catch (Exception ex) { _win.Status("Ошибка (анимации): " + ex.Message); }
    }

    private sealed class AnimEntry
    {
        public string Key { get; set; } = "";
        public string Sheet { get; set; } = "";
        public int Cols { get; set; } = 1;
        public int Rows { get; set; } = 1;
        public int Fps { get; set; } = 8;
    }
}
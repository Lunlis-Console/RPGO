using System.Text.Json;

namespace RPGGame.ClientMonoGame;

public class SettingsManager
{
    private static readonly string SettingsPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "settings.json");

    public int Width { get; set; } = 1280;
    public int Height { get; set; } = 720;
    public string Mode { get; set; } = "windowed"; // windowed | fullscreen | borderless

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true
    };

    public static SettingsManager Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var s = JsonSerializer.Deserialize<SettingsManager>(json, JsonOpts);
                if (s != null) return s;
            }
        }
        catch
        {
            // ignore load errors
        }
        return new SettingsManager();
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(this, JsonOpts);
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // ignore save errors
        }
    }
}

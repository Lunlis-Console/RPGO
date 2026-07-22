namespace RPGGame.Shared.Models;

public class NpcRecord
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public string? Data { get; set; }
}

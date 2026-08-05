namespace RPGGame.Shared.Models;

public class Zone
{
    public string Id { get; set; } = BalanceStatic.MainZoneId;
    public string Name { get; set; } = "";
    public int Width { get; set; } = 100;
    public int Height { get; set; } = 100;
    public int SpawnX { get; set; } = 50;
    public int SpawnY { get; set; } = 50;
    public bool PvpEnabled { get; set; }
}

public class WorldPortal
{
    public string Id { get; set; } = "";
    public string FromZone { get; set; } = "";
    public int FromX { get; set; }
    public int FromY { get; set; }
    public string ToZone { get; set; } = "";
    public int ToX { get; set; }
    public int ToY { get; set; }
}

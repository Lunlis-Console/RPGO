namespace LostAndDivine.Shared.Models;

public class InstanceTemplate
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string ZoneId { get; set; } = "";
    public int TimeLimitSeconds { get; set; } = 600;
    public int SpawnX { get; set; }
    public int SpawnY { get; set; }
    public string BossMonsterId { get; set; } = "";
    public int ChestX { get; set; }
    public int ChestY { get; set; }
    public int ExitX { get; set; }
    public int ExitY { get; set; }
    public int CorridorLength { get; set; } = 20;
    public int CorridorWidth { get; set; } = 5;
}

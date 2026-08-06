namespace LostAndDivine.Shared.Models;

public class HazardPosition
{
    public Guid Id { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public string Kind { get; set; } = "";
    public bool IsTriggered { get; set; }
    public double ExpiresAtMs { get; set; }
}

namespace RPGGame.Shared.Models;

public enum HazardKind
{
    Smoke,
    Snare,
    Acid
}

/// <summary>Временная ловушка/зона на клетке карты.</summary>
public class GroundHazard
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int X { get; set; }
    public int Y { get; set; }
    public string ZoneId { get; set; } = BalanceStatic.MainZoneId;
    public HazardKind Kind { get; set; }
    public DateTime ExpiresAt { get; set; }
    public Guid OwnerId { get; set; }
    public int DotDamagePerTick { get; set; }
    public HashSet<Guid> AffectedIds { get; set; } = new();
}

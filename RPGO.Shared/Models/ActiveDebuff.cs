namespace RPGGame.Shared.Models;

public enum DebuffType
{
    ArmorPenetration,
    DamageBonus,
    DamageReduction,
    AccuracyReduction,
    CleaveReady,
    AttackSpeedBonus,
    DualWieldBonus
}

public class ActiveDebuff
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public DebuffType Type { get; set; }
    public double Value { get; set; }
    public int DurationMs { get; set; }
    public int RemainingMs { get; set; }
    public string SourceSubtype { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Description { get; set; } = "";

    public static ActiveDebuff Create(DebuffType type, double value, int durationMs, string sourceSubtype, string displayName, string description = "")
        => new()
        {
            Type = type,
            Value = value,
            DurationMs = durationMs,
            RemainingMs = durationMs,
            SourceSubtype = sourceSubtype,
            DisplayName = displayName,
            Description = description
        };
}

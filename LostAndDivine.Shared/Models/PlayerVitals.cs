namespace LostAndDivine.Shared.Models;

/// <summary>
/// Жизненные показатели игрока: HP/MP, смерть, урон, скорость.
/// Вынесено из Player.cs (было 384 строки) для разделения ответственности.
/// </summary>
public sealed class PlayerVitals
{
    public int Health { get; set; } = 100;
    public int MaxHealth { get; set; } = 100;
    public int Mana { get; set; } = 100;
    public int MaxMana { get; set; } = 100;
    public bool IsDead { get; set; }
    public DateTime DeathTime { get; set; }
    public DateTime LastDamagedTime { get; set; } = DateTime.MinValue;
    public DateTime LastRegenTime { get; set; } = DateTime.MinValue;
    public int Speed { get; set; } = 1;
    public double AdminDamageMultiplier { get; set; } = 1.0;
}

namespace LostAndDivine.Shared.Models;

/// <summary>
/// Жизненные показатели игрока: HP/MP, смерть, урон, скорость.
/// Вынесено из Player.cs (было 384 строки) для разделения ответственности.
///
/// Инварианты (P2-2): Health/Mana всегда &gt;= 0; при уменьшении Max* текущее
/// значение поджимается к новому максимуму. Верхняя граница по текущему Max*
/// в сеттере Health/Mana НЕ накладывается — это сделано намеренно, чтобы не
/// ломать порядок установки MaxHealth/Health при пересчёте характеристик
/// (верхняя граница всё равно гарантируется в точках лечения/урона в сервере).
/// </summary>
public sealed class PlayerVitals
{
    private int _health = 100;
    private int _maxHealth = 100;
    private int _mana = 100;
    private int _maxMana = 100;

    public int Health
    {
        get => _health;
        set => _health = Math.Max(0, value);
    }

    public int MaxHealth
    {
        get => _maxHealth;
        set
        {
            _maxHealth = Math.Max(1, value);
            if (_health > _maxHealth) _health = _maxHealth;
        }
    }

    public int Mana
    {
        get => _mana;
        set => _mana = Math.Max(0, value);
    }

    public int MaxMana
    {
        get => _maxMana;
        set
        {
            _maxMana = Math.Max(1, value);
            if (_mana > _maxMana) _mana = _maxMana;
        }
    }

    public bool IsDead { get; set; }
    public DateTime DeathTime { get; set; }
    public DateTime LastDamagedTime { get; set; } = DateTime.MinValue;
    public DateTime LastRegenTime { get; set; } = DateTime.MinValue;
    public int Speed { get; set; } = 1;
    public double AdminDamageMultiplier { get; set; } = 1.0;
}

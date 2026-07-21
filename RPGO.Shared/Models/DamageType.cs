namespace RPGGame.Shared.Models;

/// <summary>
/// Тип урона оружия. Влияет на визуальную/звуковую обратную связь
/// и может расширяться для системы уязвимостей (броня vs тип урона).
/// </summary>
public enum DamageType
{
    /// <summary>Рубящий урон — мечи, топоры.</summary>
    Slashing,

    /// <summary>Колющий урон — кинжалы, копья.</summary>
    Piercing,

    /// <summary>Дробящий урон — булавы, молоты.</summary>
    Blunt
}

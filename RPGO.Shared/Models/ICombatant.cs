namespace RPGGame.Shared.Models;

/// <summary>
/// Единый боевой интерфейс для любой сущности, способной участвовать в бою
/// (игрок, монстр, и в будущем — NPC/босс). Позволяет обобщить расчёт урона
/// и циклы боя, не привязываясь к конкретному типу (фундамент для PvP/PvE).
/// </summary>
public interface ICombatant
{
    Guid Id { get; }
    string Name { get; }
    int X { get; }
    int Y { get; }
    int Health { get; }
    int MaxHealth { get; }
    int Level { get; }

    int GetBaseDamage();
    int GetBaseDefense();
    int GetTotalAttack();
    int GetTotalDefense();
    int RollAttackDamage();
    double GetCritChance();
    double GetCritDamage();
    double GetEvadeChance();
}

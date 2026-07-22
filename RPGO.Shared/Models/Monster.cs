namespace RPGGame.Shared.Models;

public class Monster : ICombatant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TemplateId { get; set; } = "";   // id шаблона из таблицы monsters (M0001...)
    public string Name { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int Health { get; set; }
    public int MaxHealth { get; set; }
    public int XpReward { get; set; }
    public int GoldReward { get; set; }
    public char Symbol { get; set; } = 'M';
    public int Level { get; set; }

    // Атрибуты (настраиваются в редакторе, по шаблону)
    public int Strength { get; set; } = 1;
    public int Endurance { get; set; } = 1;
    public int Agility { get; set; } = 1;
    public int Cunning { get; set; } = 1;
    public int Intellect { get; set; } = 1;
    public int Wisdom { get; set; } = 1;

    // Регенерация
    public DateTime LastDamagedTime { get; set; } = DateTime.MinValue;
    public DateTime LastRegenTime { get; set; } = DateTime.MinValue;

    // Базовые боевые параметры
    public double CritChance { get; set; } = 1.0;   // %
    public double CritDamage { get; set; } = 1.5;   // множитель
    public double EvadeChance { get; set; } = 1.0;  // %

    // --- Производные боевые характеристики ---
    public int GetBaseDamage() => 1 + (Level - 1);
    public int GetBaseDefense() => 1 + (Level - 1);

    public int GetTotalAttack()
        => GetBaseDamage() + (Strength - 1) * BalanceStatic.AttackPerStrength
           + (Agility - 1) * BalanceStatic.AttackPerAgility;

    public int GetTotalDefense()
        => GetBaseDefense() + (Endurance - 1) * BalanceStatic.DefensePerEndurance;

    public int RollAttackDamage() => GetTotalAttack();

    public double GetCritChance() => CritChance + (Cunning - 1) * BalanceStatic.CritChancePerCunning;
    public double GetCritDamage() => CritDamage + (Strength - 1) * BalanceStatic.CritDamagePerStrength;
    public double GetEvadeChance() => EvadeChance + (Cunning - 1) * BalanceStatic.EvadeChancePerCunning;

    // --- Новые характеристики (монстры могут использовать по желанию) ---
    public int GetPhysAttack() => GetTotalAttack();
    public int GetMagAttack()
        => GetBaseDamage() + (Intellect - 1) * BalanceStatic.AttackPerIntellect;
    public int GetDefense() => GetTotalDefense();
    public int GetResistance()
        => GetBaseDefense() + (Wisdom - 1) * BalanceStatic.ResistancePerWisdom;

    public int SpawnX { get; set; }
    public int SpawnY { get; set; }
    public int WanderRadius { get; set; } = 4;

    public int AggroRange { get; set; } = 5;
    public Player? AggroTarget { get; set; }

    public int MoveIntervalMs { get; set; } = 1500;
    public DateTime LastMoveTime { get; set; } = DateTime.MinValue;

    // Таблица урона: playerId → суммарный урон по этому монстру
    public Dictionary<Guid, int> DamageTracker { get; set; } = new();

    // Манекен
    public bool IsMannequin { get; set; }

    // Активные дебаффы
    public List<ActiveDebuff> ActiveDebuffs { get; set; } = new();
}

public class MonsterPosition
{
    public Guid Id { get; set; }
    public string TemplateId { get; set; } = "";
    public string Name { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int Health { get; set; }
    public int MaxHealth { get; set; }
    public char Symbol { get; set; } = 'M';
    public int Level { get; set; }
    public bool IsMannequin { get; set; }
}

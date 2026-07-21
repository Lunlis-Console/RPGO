namespace RPGGame.Shared.Models;

public class Player : ICombatant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Незнакомец";
    public int X { get; set; }
    public int Y { get; set; }
    public int Health { get; set; } = 100;
    public int MaxHealth { get; set; } = 100;
    public int Level { get; set; } = 1;
    public int Experience { get; set; }
    public int Attack { get; set; } = 10;
    public int Defense { get; set; } = 5;
    public int Gold { get; set; }

    // Мана (MP)
    public int Mana { get; set; } = 20;
    public int MaxMana { get; set; } = 20;

    // Кулдауны навыков: skillId -> время последнего применения (UTC)
    public Dictionary<string, DateTime> LastSkillUse { get; set; } = new();

    // Очередь прекаста/боя: skillId в порядке применения (без дублей)
    public List<string> QueuedSkillIds { get; set; } = new();

    public List<Item> Inventory { get; set; } = new();
    public Equipment Equipment { get; set; } = new();
    public List<QuestProgress> ActiveQuests { get; set; } = new();

    // Атрибуты
    public int Strength { get; set; } = 1;   // +Атака, +крит урон
    public int Stamina { get; set; } = 1;    // +Защита, +MaxHP
    public int Agility { get; set; } = 1;    // +уклонение, +шанс крита
    public int Cunning { get; set; } = 1;    // -цены в магазине
    public int Wisdom { get; set; } = 1;     // +урон магией
    public int Will { get; set; } = 1;       // +мана
    public int AttributePoints { get; set; }

    // Базовые боевые параметры (редактируются позже бонусами экипировки/умений)
    public double BaseCritChance { get; set; } = 1.0;   // %
    public double BaseCritDamage { get; set; } = 1.5;   // множитель
    public double BaseEvadeChance { get; set; } = 1.0;  // %

    // --- Производные боевые характеристики ---
    public int GetBaseDamage() => 1 + (Level - 1); // на 1 уровне = 1, +1 за каждый уровень
    public int GetBaseDefense() => 1 + (Level - 1); // на 1 уровне = 1, +1 за каждый уровень

    // Эффективные атрибуты (с учётом бонусов экипировки)
    public int GetEffStrength() => Strength + Equipment.GetBonusStrength();
    public int GetEffStamina() => Stamina + Equipment.GetBonusStamina();
    public int GetEffAgility() => Agility + Equipment.GetBonusAgility();
    public int GetEffCunning() => Cunning + Equipment.GetBonusCunning();
    public int GetEffWisdom() => Wisdom + Equipment.GetBonusWisdom();
    public int GetEffWill() => Will + Equipment.GetBonusWill();

    public int GetTotalAttack()
        => GetBaseDamage() + (GetEffStrength() - 1) * 2 + Equipment.GetBonusAttack();

    public int GetTotalDefense()
        => GetBaseDefense() + (GetEffStamina() - 1) * 1 + Equipment.GetBonusDefense();

    public double GetCritChance() => BaseCritChance + (GetEffAgility() - 1) * 1.0 + Equipment.GetBonusCritChance(); // %
    public double GetCritDamage() => BaseCritDamage + (GetEffStrength() - 1) * 0.05 + Equipment.GetBonusCritDamage();
    public double GetEvadeChance() => BaseEvadeChance + (GetEffAgility() - 1) * 1.0 + Equipment.GetBonusEvadeChance(); // %

    public int Speed { get; set; } = 1;   // определяет интервал перемещения

    // Регенерация
    public DateTime LastDamagedTime { get; set; } = DateTime.MinValue;
    public DateTime LastRegenTime { get; set; } = DateTime.MinValue;

    // Компоненты состояний
    public MovementState Movement { get; set; } = new();
    public CombatState Combat { get; set; } = new();
    public InteractionState Interaction { get; set; } = new();

    // Панель быстрого доступа (10 слотов, хранятся ID предметов)
    public List<string?> HotbarSlots { get; set; } = new(10) { null, null, null, null, null, null, null, null, null, null };

    public List<Item> BuybackItems { get; set; } = new();  // проданное, доступно для выкупа (сбрасывается при перезаходе)

    // Пати
    public Guid? PartyId { get; set; }

    // Обмен
    public bool IsTrading { get; set; }

    // Администрирование
    public bool IsAdmin { get; set; }
}
namespace RPGGame.Shared.Models;

public class PlayerData
{
    public int X { get; set; } = -1;
    public int Y { get; set; } = -1;
    public int Health { get; set; }
    public int MaxHealth { get; set; }
    public int Level { get; set; } = 1;
    public int Experience { get; set; }
    public int Gold { get; set; }
    public int Strength { get; set; } = 1;
    public int Endurance { get; set; } = 1;
    public int Agility { get; set; } = 1;
    public int Cunning { get; set; } = 1;
    public int Intellect { get; set; } = 1;
    public int Wisdom { get; set; } = 1;
    public int AttributePoints { get; set; }
    public int Speed { get; set; } = 1;
    public List<Item> Inventory { get; set; } = new();
    public Equipment Equipment { get; set; } = new();
    public List<QuestProgress> ActiveQuests { get; set; } = new();
    public List<string?> HotbarSlots { get; set; } = new(10) { null, null, null, null, null, null, null, null, null, null };
    public Guid? PartyId { get; set; }
}

public class Account
{
    public string Login { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string PlayerName { get; set; } = "";
    public int Gold { get; set; }
    public PlayerData PlayerData { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime LastLogin { get; set; } = DateTime.Now;
    public bool IsAdmin { get; set; }
    public bool IsBanned { get; set; }
    public string BanReason { get; set; } = "";
    public Guid? PartyId { get; set; }
}

public class Item
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TemplateId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public int Quantity { get; set; } = 1;
    public int Value { get; set; }
    public int MaxHealthBonus { get; set; }
    public int HealAmount { get; set; }
    public string Description { get; set; } = "";
    public int Stock { get; set; } = 1;
    public int MaxStack { get; set; } = 10;

    // Бонусы к первичным атрибутам
    public int BonusStrength { get; set; }
    public int BonusEndurance { get; set; }
    public int BonusAgility { get; set; }
    public int BonusCunning { get; set; }
    public int BonusIntellect { get; set; }
    public int BonusWisdom { get; set; }

    // Бонусы к вторичным характеристикам
    public int BonusPhysAttack { get; set; }
    public int BonusMagAttack { get; set; }
    public int BonusDefense { get; set; }
    public int BonusResistance { get; set; }
    public double BonusCritChance { get; set; }
    public double BonusCritDamage { get; set; }
    public double BonusEvadeChance { get; set; }
    public double BonusAttackSpeed { get; set; }

    // Тип урона оружия
    public string DamageType { get; set; } = "";

    // Подтип оружия
    public string WeaponSubtype { get; set; } = "";

    // Модификатор скорости атаки оружия
    public double AttackSpeedModifier { get; set; } = 1.0;

    // Двуручное оружие
    public bool TwoHanded { get; set; }

    public Item Clone() => (Item)MemberwiseClone();
}

namespace RPGGame.Shared.Models;

public class Account
{
    public string Login { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string PlayerName { get; set; } = "";
    public PlayerData PlayerData { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime LastLogin { get; set; } = DateTime.Now;
}

public class PlayerData
{
    public int Level { get; set; } = 1;
    public int Experience { get; set; } = 0;
    public int Health { get; set; } = 100;
    public int MaxHealth { get; set; } = 100;
    public int Attack { get; set; } = 10;
    public int Defense { get; set; } = 5;
    public int Gold { get; set; }
    public List<Item> Inventory { get; set; } = new();
    public Equipment Equipment { get; set; } = new();
    public List<QuestProgress> ActiveQuests { get; set; } = new();

    // Атрибуты
    public int Strength { get; set; } = 1;
    public int Stamina { get; set; } = 1;
    public int Agility { get; set; } = 1;
    public int Cunning { get; set; } = 1;
    public int Wisdom { get; set; } = 1;
    public int Will { get; set; } = 1;
    public int AttributePoints { get; set; }

    public int Speed { get; set; } = 1;

    public int X { get; set; } = -1;
    public int Y { get; set; } = -1;

    // Панель быстрого доступа (10 слотов, хранятся ID предметов)
    public List<string?> HotbarSlots { get; set; } = new(10) { null, null, null, null, null, null, null, null, null, null };

    // Пати
    public Guid? PartyId { get; set; }
}

public class Item
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TemplateId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public int Value { get; set; }
    public int Attack { get; set; }
    public int Defense { get; set; }
    public int MaxHealthBonus { get; set; }
    public int HealAmount { get; set; }
    public string Description { get; set; } = "";
    public int Stock { get; set; } = 1;   // запас у торговца (для расходников = 99)
    public int MaxStack { get; set; } = 10; // макс. кол-во в одном слоте инвентаря/хотбара

    // Бонусы от экипировки к атрибутам
    public int BonusStrength { get; set; }
    public int BonusStamina { get; set; }
    public int BonusAgility { get; set; }
    public int BonusCunning { get; set; }
    public int BonusWisdom { get; set; }
    public int BonusWill { get; set; }

    // Бонусы к боевым параметрам
    public double BonusCritChance { get; set; }   // %
    public double BonusCritDamage { get; set; }   // множитель
    public double BonusEvadeChance { get; set; }  // %
}
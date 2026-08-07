namespace LostAndDivine.Shared.Models;

public class PlayerData
{
    public int X { get; set; } = -1;
    public int Y { get; set; } = -1;
    public int Health { get; set; } = 100;
    public int MaxHealth { get; set; } = 100;
    public int Mana { get; set; } = 100;
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
    public int SkillPoints { get; set; }
    public List<string> LearnedSkills { get; set; } = new();
    public Dictionary<string, int> SkillRanks { get; set; } = new(); // SkillId > ���� (1=������)
    public int Speed { get; set; } = 1;
    public List<Item> Inventory { get; set; } = new();
    public Equipment Equipment { get; set; } = new();
    public List<QuestProgress> ActiveQuests { get; set; } = new();
    public List<string> CompletedQuestIds { get; set; } = new();
    public List<string?> HotbarSlots { get; set; } = new(10) { null, null, null, null, null, null, null, null, null, null };
    public Guid? PartyId { get; set; }
    public string CurrentZoneId { get; set; } = BalanceStatic.MainZoneId;
}

public class CharacterModel
{
    public string Name { get; set; } = "";
    public string AccountLogin { get; set; } = "";
    public CharacterClass Class { get; set; }
    public int X { get; set; } = -1;
    public int Y { get; set; } = -1;
    public int Health { get; set; } = 100;
    public int MaxHealth { get; set; } = 100;
    public int Mana { get; set; } = 100;
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
    public int SkillPoints { get; set; }
    public List<string> LearnedSkills { get; set; } = new();
    public Dictionary<string, int> SkillRanks { get; set; } = new();
    public int Speed { get; set; } = 1;
    public List<Item> Inventory { get; set; } = new();
    public Equipment Equipment { get; set; } = new();
    public List<QuestProgress> ActiveQuests { get; set; } = new();
    public List<string> CompletedQuestIds { get; set; } = new();
    public List<string?> HotbarSlots { get; set; } = new(10) { null, null, null, null, null, null, null, null, null, null };
    public string CurrentZoneId { get; set; } = BalanceStatic.MainZoneId;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

public class Account
{
    public string Login { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime LastLogin { get; set; } = DateTime.Now;
    public bool IsAdmin { get; set; }
    public bool IsBanned { get; set; }
    public string BanReason { get; set; } = "";
}

public class CharacterInfo
{
    public string Name { get; set; } = "";
    public int Level { get; set; }
    public string Class { get; set; } = "";
    public string Zone { get; set; } = "";
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
    public int RestoreMana { get; set; }
    public string Description { get; set; } = "";
    public int Stock { get; set; } = 1;
    public int MaxStack { get; set; } = 10;
    public bool IsBuyback { get; set; }

    // ������ � ��������� ���������
    public int BonusStrength { get; set; }
    public int BonusEndurance { get; set; }
    public int BonusAgility { get; set; }
    public int BonusCunning { get; set; }
    public int BonusIntellect { get; set; }
    public int BonusWisdom { get; set; }

    // ������ � ��������� ���������������
    public int BonusPhysAttack { get; set; }
    public int BonusMagAttack { get; set; }
    public int BonusDefense { get; set; }
    public int BonusResistance { get; set; }
    public double BonusCritChance { get; set; }
    public double BonusCritDamage { get; set; }
    public double BonusEvadeChance { get; set; }
    public double BonusAttackSpeed { get; set; }
    public double BonusBlockChance { get; set; }
    public double BonusParryChance { get; set; }

    // ��� ����� ������
    public string DamageType { get; set; } = "";

    // ������ ������
    public string WeaponSubtype { get; set; } = "";

    // ��������� ������ (��������� �� WeaponSubtype)
    public WeaponCategory Category => WeaponCategoryExtensions.Parse(WeaponSubtype);

    // ����� ������ (��������� �� Category)
    public ItemClass Class => Category.GetItemClass();

    // ��������� ������� (0 = ��� �����������)
    public int RequiredLevel { get; set; }

    // �������� ����� ������
    public int DamageMin { get; set; }
    public int DamageMax { get; set; }

    // ����������� �������� ����� ������
    public double AttackSpeedModifier { get; set; } = 1.0;

    // ��������� ������
    public bool TwoHanded { get; set; }

    // ��������� ����� (1 = ������� ���, 3 = ��� � �.�.)
    public int AttackRange { get; set; } = 1;

    // �������� �������� (Common/Uncommon/Rare/Epic) � ������������ �� ��������
    public ItemQuality Quality => ItemQualityExtensions.ParseFromDescription(Description);

    public Item Clone() => (Item)MemberwiseClone();
}

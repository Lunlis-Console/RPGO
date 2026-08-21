using Microsoft.Xna.Framework;
using LostAndDivine.Shared.Models;

namespace LostAndDivine.ClientMonoGame.Rendering;

public static class ItemTooltip
{
    private static readonly Color PriceColor = new Color(230, 220, 80);
    private static readonly Color QualityUncommon = new Color(76, 175, 80);
    private static readonly Color QualityRare = new Color(66, 165, 245);
    private static readonly Color QualityEpic = new Color(171, 71, 188);
    private static readonly Color RequiredLevelBad = new Color(235, 75, 75);
    private static readonly Color RequiredLevelGood = new Color(110, 200, 90);
    private static readonly Color SectionColor = new Color(150, 165, 210);

    /// <summary>Текущий уровень игрока для подсветки требуемого уровня предметов.</summary>
    public static int PlayerLevel { get; set; } = 1;

    private static Color QualityColor(ItemQuality q) => q switch
    {
        ItemQuality.Uncommon => QualityUncommon,
        ItemQuality.Rare => QualityRare,
        ItemQuality.Epic => QualityEpic,
        _ => Color.White
    };
    public static string TypeLabel(string t) => t switch
    {
        "weapon" => "Оружие",
        "twohand" => "Двуручное оружие",
        "shield" => "Щит",
        "helmet" => "Шлем",
        "cloak" => "Плащ",
        "chest" => "Нагрудник",
        "legs" => "Поножи",
        "boots" => "Сапоги",
        "glove" => "Перчатки",
        "belt" => "Пояс",
        "necklace" => "Ожерелье",
        "ring" => "Кольцо",
        "armor" => "Броня",
        "consumable" => "Расходник",
        "collectible" => "Коллекция",
        "material" => "Материал",
        "trophy" => "Трофей",
        _ => t
    };

    public static string WeaponCategoryLabel(WeaponCategory category) => category switch
    {
        WeaponCategory.Sword => "Меч",
        WeaponCategory.Greatsword => "Двуручный меч",
        WeaponCategory.Axe => "Топор",
        WeaponCategory.Greataxe => "Секира",
        WeaponCategory.Mace => "Булава",
        WeaponCategory.Hammer => "Молот",
        WeaponCategory.Greathammer => "Двуручный молот",
        WeaponCategory.Dagger => "Кинжал",
        WeaponCategory.Halberd => "Алебарда",
        WeaponCategory.Spear => "Копьё",
        WeaponCategory.Staff => "Посох",
        WeaponCategory.Bow => "Лук",
        WeaponCategory.Grimoire => "Гримуар",
        WeaponCategory.Sphere => "Сфера",
        WeaponCategory.Shield => "Щит",
        _ => ""
    };

    public static string DamageTypeLabel(string damageType) => damageType.ToLower() switch
    {
        "slashing" => "Рубящий",
        "piercing" => "Колющий",
        "blunt" => "Дробящий",
        _ => damageType
    };

    public static string WeaponProcDescription(WeaponCategory category) => category switch
    {
        WeaponCategory.Dagger or WeaponCategory.Spear => "5% шанс: Пронзание (снижает защиту)",
        WeaponCategory.Sword or WeaponCategory.Greatsword => "5% шанс: Рассекающий удар (урон по 3 клеткам)",
        WeaponCategory.Axe or WeaponCategory.Greataxe or WeaponCategory.Halberd => "5% шанс: Свирепость (+к урону)",
        WeaponCategory.Mace => "5% шанс: Обезоруживание (снижает урон)",
        WeaponCategory.Hammer or WeaponCategory.Greathammer => "5% шанс: Контузия (снижает точность)",
        WeaponCategory.Grimoire => "Магическое оружие. С оружием в правой руке — только бафф.",
        WeaponCategory.Sphere => "Магическое оружие. С оружием в правой руке — только бафф.",
        _ => ""
    };

    public static List<TooltipLine> BuildLines(Item item, int? overrideValue = null, int? stockOverride = null)
    {
        var lines = new List<TooltipLine>();

        bool isWeapon = item.Type == "weapon" || item.Type == "twohand";
        bool isCasterShield = item.Type == "shield" && Equipment.IsCasterOffhand(item);
        bool isGear = isWeapon || item.Type == "shield" || item.Type is "helmet" or "cloak" or "chest" or "legs"
            or "boots" or "glove" or "belt" or "necklace" or "ring" or "armor";

        lines.Add(item.Name);
        lines.Add(new TooltipLine($"Тип: {TypeLabel(item.Type)}"));

        AddBaseStatLines(lines, item);
        AddSecondaryStatLines(lines, item);
        AddAttributeLines(lines, item);

        if (isGear || item.Quality != ItemQuality.Common)
        {
            string qualLabel = ItemQualityExtensions.Label(item.Quality);
            lines.Add(new TooltipLine($"Качество: {qualLabel}", QualityColor(item.Quality)));
        }

        if (item.EnhancementLevel > 0)
        {
            lines.Add(new TooltipLine($"Заточка: +{item.EnhancementLevel}", new Color(255, 170, 60)));
            var enh = new List<string>();
            if (item.BonusPhysAttack != 0) enh.Add($"Физ. атака +{item.Enhanced(item.BonusPhysAttack) - item.BonusPhysAttack}");
            if (item.BonusMagAttack != 0) enh.Add($"Маг. атака +{item.Enhanced(item.BonusMagAttack) - item.BonusMagAttack}");
            if (item.BonusDefense != 0) enh.Add($"Защита +{item.Enhanced(item.BonusDefense) - item.BonusDefense}");
            if (item.BonusResistance != 0) enh.Add($"Сопротивление +{item.Enhanced(item.BonusResistance) - item.BonusResistance}");
            if (item.Defense != 0) enh.Add($"Броня +{item.Enhanced(item.Defense) - item.Defense}");
            if (item.MagicDefense != 0) enh.Add($"Маг. защита +{item.Enhanced(item.MagicDefense) - item.MagicDefense}");
            if (item.MaxHealthBonus != 0) enh.Add($"Макс. HP +{item.Enhanced(item.MaxHealthBonus) - item.MaxHealthBonus}");
            if (item.MaxManaBonus != 0) enh.Add($"Макс. мана +{item.Enhanced(item.MaxManaBonus) - item.MaxManaBonus}");
            if (item.DamageMax > 0) enh.Add($"Урон +{item.Enhanced(item.DamageMax) - item.DamageMax}");
            if (enh.Count > 0) lines.Add(new TooltipLine(string.Join(", ", enh), new Color(255, 200, 120)));
        }

        if (item.RequiredLevel > 0)
        {
            var levelColor = item.RequiredLevel <= PlayerLevel ? RequiredLevelGood : RequiredLevelBad;
            lines.Add(new TooltipLine($"Требуемый уровень: {item.RequiredLevel}", levelColor));
        }

        string cleanDesc = ItemQualityExtensions.StripQualityPrefix(item.Description);
        if (!string.IsNullOrEmpty(cleanDesc))
            lines.Add(cleanDesc);

        if (stockOverride.HasValue && stockOverride.Value > 1)
            lines.Add($"В наличии: {stockOverride.Value}");

        int price = overrideValue ?? item.Value;
        lines.Add(new TooltipLine($"Цена: {price} золота", PriceColor));

        return lines;
    }

    public static List<string> BuildLinesForTrade(string name, string type, int value, int attack, int defense, int maxHealth, int heal, int restoreMana, string description)
    {
        var lines = new List<string>
        {
            name,
            $"Тип: {TypeLabel(type)}",
            $"Цена: {value} золота"
        };

        if (attack > 0) lines.Add($"Физ.Атака: +{attack}");
        if (defense > 0) lines.Add($"Физ. защита: +{defense}");
        if (maxHealth > 0) lines.Add($"Здоровье: +{maxHealth}");
        if (heal > 0) lines.Add($"Лечение: +{heal}");
        if (restoreMana > 0) lines.Add($"Мана: +{restoreMana}");
        if (!string.IsNullOrEmpty(description)) lines.Add(description);

        return lines;
    }

    public static List<string> BuildLinesForLoot(string name, string type, int value, string description)
    {
        return new List<string>
        {
            name,
            $"Тип: {TypeLabel(type)}",
            $"Ценность: {value}",
            description
        };
    }

    private static void AddBaseStatLines(List<TooltipLine> lines, Item item)
    {
        bool isWeapon = item.Type == "weapon" || item.Type == "twohand";
        bool isShield = item.Type == "shield";
        bool isCasterShield = isShield && Equipment.IsCasterOffhand(item);

        bool hasBase =
            (isWeapon || isCasterShield) && item.DamageMax > 0
            || (isWeapon || isShield) && item.AttackRange > 1
            || item.Defense > 0 || item.MagicDefense > 0
            || item.MaxHealthBonus > 0 || item.MaxManaBonus > 0
            || item.HealAmount > 0 || item.RestoreMana > 0
            || isWeapon || isCasterShield;
        if (!hasBase) return;

        lines.Add(new TooltipLine("Базовые характеристики", SectionColor));

        if ((isWeapon || isCasterShield) && item.DamageMax > 0)
        {
            if (item.DamageMin == item.DamageMax)
                lines.Add($"Урон: {item.DamageMax}");
            else
                lines.Add($"Урон: {item.DamageMin}-{item.DamageMax}");
        }

        if (isWeapon || isCasterShield)
        {
            if (isWeapon)
            {
                string handLabel = item.TwoHanded || item.Type == "twohand" ? "Двуручное" : "Одноручное";
                lines.Add($"Вид: {handLabel}");
            }
            else
            {
                lines.Add("Вид: Левая рука");
            }
            if (item.Category != WeaponCategory.None)
                lines.Add($"Тип оружия: {WeaponCategoryLabel(item.Category)}");
            if (!string.IsNullOrEmpty(item.DamageType) && item.DamageType != "none")
                lines.Add($"Тип урона: {DamageTypeLabel(item.DamageType)}");
            if (item.AttackSpeedModifier > 0 && item.AttackSpeedModifier != 1.0)
                lines.Add($"Скор. атаки: {item.AttackSpeedModifier:F1}x");
            if (item.Category != WeaponCategory.None)
            {
                string proc = WeaponProcDescription(item.Category);
                if (proc.Length > 0) lines.Add(proc);
            }
        }

        if ((isWeapon || isShield) && item.AttackRange > 1)
            lines.Add($"Дальность: {item.AttackRange}");

        if (item.Defense > 0) lines.Add($"Физ. защита: {item.Defense}");
        if (item.MagicDefense > 0) lines.Add($"Маг. защита: {item.MagicDefense}");
        if (item.MaxHealthBonus > 0) lines.Add($"Бонус к HP: +{item.MaxHealthBonus}");
        if (item.MaxManaBonus > 0) lines.Add($"Бонус к MP: +{item.MaxManaBonus}");
        if (item.HealAmount > 0) lines.Add($"Лечение: +{item.HealAmount}");
        if (item.RestoreMana > 0) lines.Add($"Восст. маны: +{item.RestoreMana}");
    }

    private static void AddSecondaryStatLines(List<TooltipLine> lines, Item item)
    {
        bool hasSec =
            item.BonusPhysAttack > 0 || item.BonusMagAttack > 0
            || item.BonusDefense > 0 || item.BonusResistance > 0
            || item.BonusAttackSpeed > 0
            || item.BonusCritChance > 0 || item.BonusCritDamage > 0
            || item.BonusEvadeChance > 0
            || item.BonusBlockChance > 0 || item.BonusParryChance > 0
            || item.BonusAccuracy > 0 || item.BonusTenacity > 0
            || item.BonusArmorPenetration > 0 || item.BonusCooldownReduction > 0
            || item.BonusHpRegen > 0 || item.BonusMpRegen > 0;
        if (!hasSec) return;

        lines.Add(new TooltipLine("Доп. характеристики", SectionColor));

        if (item.BonusPhysAttack > 0) lines.Add($"+Физ. атака: {item.BonusPhysAttack}");
        if (item.BonusMagAttack > 0) lines.Add($"+Маг. атака: {item.BonusMagAttack}");
        if (item.BonusDefense > 0) lines.Add($"+Физ. защита: {item.BonusDefense}");
        if (item.BonusResistance > 0) lines.Add($"+Маг. защита: {item.BonusResistance}");
        if (item.BonusAttackSpeed > 0) lines.Add($"+Скор. атк %: {item.BonusAttackSpeed}");
        if (item.BonusCritChance > 0) lines.Add($"+Крит %: {item.BonusCritChance}");
        if (item.BonusCritDamage > 0) lines.Add($"+Крит урон %: {item.BonusCritDamage}");
        if (item.BonusEvadeChance > 0) lines.Add($"+Уклон %: {item.BonusEvadeChance}");
        if (item.BonusBlockChance > 0) lines.Add($"+Блок %: {item.BonusBlockChance}");
        if (item.BonusParryChance > 0) lines.Add($"+Парир %: {item.BonusParryChance}");
        if (item.BonusAccuracy > 0) lines.Add($"+Точность %: {item.BonusAccuracy}");
        if (item.BonusTenacity > 0) lines.Add($"+Стойк %: {item.BonusTenacity}");
        if (item.BonusArmorPenetration > 0) lines.Add($"+Пробив %: {item.BonusArmorPenetration}");
        if (item.BonusCooldownReduction > 0) lines.Add($"+Откат %: {item.BonusCooldownReduction}");
        if (item.BonusHpRegen > 0) lines.Add($"+Реген ХП %: {item.BonusHpRegen}");
        if (item.BonusMpRegen > 0) lines.Add($"+Реген МП %: {item.BonusMpRegen}");
    }

    private static void AddAttributeLines(List<TooltipLine> lines, Item item)
    {
        bool hasAttr = item.BonusStrength > 0 || item.BonusEndurance > 0 || item.BonusAgility > 0
                    || item.BonusCunning > 0 || item.BonusIntellect > 0 || item.BonusWisdom > 0;
        if (!hasAttr) return;

        lines.Add(new TooltipLine("Атрибуты", SectionColor));

        if (item.BonusStrength > 0) lines.Add($"Сила +{item.BonusStrength}");
        if (item.BonusEndurance > 0) lines.Add($"Выносливость +{item.BonusEndurance}");
        if (item.BonusAgility > 0) lines.Add($"Ловкость +{item.BonusAgility}");
        if (item.BonusCunning > 0) lines.Add($"Хитрость +{item.BonusCunning}");
        if (item.BonusIntellect > 0) lines.Add($"Интеллект +{item.BonusIntellect}");
        if (item.BonusWisdom > 0) lines.Add($"Мудрость +{item.BonusWisdom}");
    }
}

using Microsoft.Xna.Framework;
using LostAndDivine.Shared.Models;

namespace LostAndDivine.ClientMonoGame.Rendering;

public static class ItemTooltip
{
    private static readonly Color PriceColor = new Color(230, 220, 80);
    private static readonly Color QualityUncommon = new Color(76, 175, 80);
    private static readonly Color QualityRare = new Color(66, 165, 245);
    private static readonly Color QualityEpic = new Color(171, 71, 188);
    private static readonly Color RequiredLevelColor = new Color(255, 140, 80);

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
        "accessory" => "Аксессуар",
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
        bool hasQuality = isWeapon || isCasterShield || item.Type == "shield";

        lines.Add(item.Name);
        lines.Add(new TooltipLine($"Тип: {TypeLabel(item.Type)}"));

        if (hasQuality)
        {
            string qualLabel = ItemQualityExtensions.Label(item.Quality);
            lines.Add(new TooltipLine($"Качество: {qualLabel}", QualityColor(item.Quality)));
        }

        if (item.RequiredLevel > 0)
            lines.Add(new TooltipLine($"Требуемый уровень: {item.RequiredLevel}", RequiredLevelColor));

        if (isWeapon || isCasterShield)
        {
            if (isWeapon)
            {
                string handLabel = item.TwoHanded || item.Type == "twohand" ? "Двуручное" : "Одноручное";
                lines.Add($"Вид: {handLabel}");
            }
            else
            {
                lines.Add($"Вид: Левая рука");
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

        AddStatLines(lines, item);

        if (stockOverride.HasValue && stockOverride.Value > 1)
            lines.Add($"В наличии: {stockOverride.Value}");

        if (!hasQuality && !string.IsNullOrEmpty(item.Description))
            lines.Add(item.Description);

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
        if (defense > 0) lines.Add($"Защита: +{defense}");
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

    private static void AddStatLines(List<TooltipLine> lines, Item item)
    {
        bool isWeapon = item.Type == "weapon" || item.Type == "twohand";
        bool isShield = item.Type == "shield";
        if (isWeapon && item.DamageMax > 0)
        {
            if (item.DamageMin == item.DamageMax)
                lines.Add($"Урон: {item.DamageMax}");
            else
                lines.Add($"Урон: {item.DamageMin}-{item.DamageMax}");
        }
        if (isShield && item.Category is WeaponCategory.Grimoire or WeaponCategory.Sphere && item.DamageMax > 0)
        {
            if (item.DamageMin == item.DamageMax)
                lines.Add($"Урон: {item.DamageMax}");
            else
                lines.Add($"Урон: {item.DamageMin}-{item.DamageMax}");
        }
        if ((isWeapon || isShield) && item.AttackRange > 1) lines.Add($"Дальность: {item.AttackRange}");
        else if (item.BonusPhysAttack > 0) lines.Add($"Физ.Атака: +{item.BonusPhysAttack}");
        if (item.BonusMagAttack > 0) lines.Add($"Маг.Атака: +{item.BonusMagAttack}");
        if (item.BonusDefense > 0) lines.Add($"Защита: +{item.BonusDefense}");
        if (item.BonusResistance > 0) lines.Add($"Сопротивление: +{item.BonusResistance}");
        if (item.BonusCritChance > 0) lines.Add($"Крит. шанс: +{item.BonusCritChance}%");
        if (item.BonusCritDamage > 0) lines.Add($"Крит. урон: +{item.BonusCritDamage}%");
        if (item.BonusEvadeChance > 0) lines.Add($"Уклонение: +{item.BonusEvadeChance}%");
        if (item.BonusBlockChance > 0) lines.Add($"Блок: +{item.BonusBlockChance}%");
        if (item.BonusParryChance > 0) lines.Add($"Парирование: +{item.BonusParryChance}%");
        if (item.BonusAttackSpeed > 0) lines.Add($"Скор. атаки: +{item.BonusAttackSpeed}");

        bool hasAttr = item.BonusStrength > 0 || item.BonusEndurance > 0 || item.BonusAgility > 0
                    || item.BonusCunning > 0 || item.BonusIntellect > 0 || item.BonusWisdom > 0;
        if (hasAttr)
        {
            var attrs = new List<string>();
            if (item.BonusStrength > 0) attrs.Add($"Сила +{item.BonusStrength}");
            if (item.BonusEndurance > 0) attrs.Add($"Выносл. +{item.BonusEndurance}");
            if (item.BonusAgility > 0) attrs.Add($"Ловк. +{item.BonusAgility}");
            if (item.BonusCunning > 0) attrs.Add($"Хитр. +{item.BonusCunning}");
            if (item.BonusIntellect > 0) attrs.Add($"Инт. +{item.BonusIntellect}");
            if (item.BonusWisdom > 0) attrs.Add($"Мудр. +{item.BonusWisdom}");
            lines.Add(string.Join(", ", attrs));
        }

        if (item.MaxHealthBonus > 0) lines.Add($"Здоровье: +{item.MaxHealthBonus}");
        if (item.HealAmount > 0) lines.Add($"Лечение: +{item.HealAmount}");
        if (item.RestoreMana > 0) lines.Add($"Мана: +{item.RestoreMana}");
    }
}

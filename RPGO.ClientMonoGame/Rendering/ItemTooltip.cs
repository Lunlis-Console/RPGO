using RPGGame.Shared.Models;

namespace RPGGame.ClientMonoGame.Rendering;

public static class ItemTooltip
{
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
        "glove_r" => "Правая перчатка",
        "glove_l" => "Левая перчатка",
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

    public static string WeaponSubtypeLabel(string subtype) => subtype.ToLower() switch
    {
        "sword" => "Меч",
        "axe" => "Топор",
        "mace" => "Булава",
        "hammer" => "Молот",
        "dagger" => "Кинжал",
        _ => subtype
    };

    public static string DamageTypeLabel(string damageType) => damageType.ToLower() switch
    {
        "slashing" => "Рубящий",
        "piercing" => "Колющий",
        "blunt" => "Дробящий",
        _ => damageType
    };

    public static string WeaponProcDescription(string subtype) => subtype.ToLower() switch
    {
        "dagger" => "5% шанс: Пронзание (снижает защиту)",
        "sword" => "5% шанс: Рассекающий удар (урон по 3 клеткам)",
        "axe" => "5% шанс: Свирепость (+к урону)",
        "mace" => "5% шанс: Обезоруживание (снижает урон)",
        "hammer" => "5% шанс: Контузия (снижает точность)",
        _ => ""
    };

    public static List<string> BuildLines(Item item, int? overrideValue = null, int? stockOverride = null)
    {
        var lines = new List<string>();

        lines.Add(item.Name);
        lines.Add($"Тип: {TypeLabel(item.Type)}");

        int price = overrideValue ?? item.Value;
        lines.Add($"Цена: {price} золота");

        bool isWeapon = item.Type == "weapon" || item.Type == "twohand";

        if (isWeapon)
        {
            string handLabel = item.TwoHanded || item.Type == "twohand" ? "Двуручное" : "Одноручное";
            lines.Add($"Вид: {handLabel}");
            if (!string.IsNullOrEmpty(item.WeaponSubtype))
                lines.Add($"Тип оружия: {WeaponSubtypeLabel(item.WeaponSubtype)}");
            if (!string.IsNullOrEmpty(item.DamageType) && item.DamageType != "none")
                lines.Add($"Тип урона: {DamageTypeLabel(item.DamageType)}");
            if (item.AttackSpeedModifier > 0 && item.AttackSpeedModifier != 1.0)
                lines.Add($"Скор. атаки: {item.AttackSpeedModifier:F1}x");
            if (!string.IsNullOrEmpty(item.WeaponSubtype))
            {
                string proc = WeaponProcDescription(item.WeaponSubtype);
                if (proc.Length > 0) lines.Add(proc);
            }
        }

        AddStatLines(lines, item);

        if (stockOverride.HasValue && stockOverride.Value > 1)
            lines.Add($"В наличии: {stockOverride.Value}");

        if (!string.IsNullOrEmpty(item.Description))
            lines.Add(item.Description);

        return lines;
    }

    public static List<string> BuildLinesForTrade(string name, string type, int value, int attack, int defense, int maxHealth, int heal, string description)
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

    private static void AddStatLines(List<string> lines, Item item)
    {
        bool isWeapon = item.Type == "weapon" || item.Type == "twohand";
        if (isWeapon && item.DamageMax > 0)
        {
            if (item.DamageMin == item.DamageMax)
                lines.Add($"Урон: {item.DamageMax}");
            else
                lines.Add($"Урон: {item.DamageMin}-{item.DamageMax}");
        }
        else if (item.BonusPhysAttack > 0) lines.Add($"Физ.Атака: +{item.BonusPhysAttack}");
        if (item.BonusMagAttack > 0) lines.Add($"Маг.Атака: +{item.BonusMagAttack}");
        if (item.BonusDefense > 0) lines.Add($"Защита: +{item.BonusDefense}");
        if (item.BonusResistance > 0) lines.Add($"Сопротивление: +{item.BonusResistance}");
        if (item.BonusCritChance > 0) lines.Add($"Крит. шанс: +{item.BonusCritChance}%");
        if (item.BonusCritDamage > 0) lines.Add($"Крит. урон: +{item.BonusCritDamage}%");
        if (item.BonusEvadeChance > 0) lines.Add($"Уклонение: +{item.BonusEvadeChance}%");
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
    }
}

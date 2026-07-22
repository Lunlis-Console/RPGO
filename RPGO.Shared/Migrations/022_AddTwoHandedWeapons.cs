using System.Globalization;
using FluentMigrator;

namespace RPGGame.Shared.Migrations;

/// <summary>
/// Добавляем двуручное оружие: двуручные мечи, секиры, двуручные молоты, алебарды, копья.
/// 5 типов × 5 тиров = 25 предметов (I0401–I0425).
/// </summary>
[Migration(22)]
public class AddTwoHandedWeapons : ForwardOnlyMigration
{
    private static readonly (string id, string name, string subtype, string dmgType, double speed,
        int value, int physAtk, int dmgMin, int dmgMax,
        int str, int end, int agi, int cun,
        double critChance, double critDmg, double evadeChance,
        string desc)[] Weapons =
        {
        // ===== ДВУРУЧНЫЕ МЕЧИ (рубящий, greatsword) =====
        ("I0401", "Ржавый двуручный меч",     "greatsword", "slashing", 0.75,   3,  2, 3,  3, 0, 0, 0, 0, 0,   0,    0, "Тяжёлый ржавый клинок."),
        ("I0402", "Железный двуручный меч",     "greatsword", "slashing", 0.75,  10,  5, 7,  7, 1, 0, 0, 0, 0,   0,    0, "Крепкий железный двуручник."),
        ("I0403", "Стальной двуручный меч",     "greatsword", "slashing", 0.75,  30,  8, 10, 10, 2, 0, 1, 0, 1,   0,    0, "Острый стальной двуручник."),
        ("I0404", "Эбонитовый двуручный меч",   "greatsword", "slashing", 0.75,  75, 12, 14, 14, 3, 0, 1, 0, 1, 0.15,  0, "Тёмный двуручник из эбонита."),
        ("I0405", "Мифриловый двуручный меч",   "greatsword", "slashing", 0.75, 220, 18, 22, 22, 5, 0, 2, 0, 2, 0.3,   0, "Легендарный двуручный клинок."),

        // ===== СЕКИРЫ (рубящий, greataxe) =====
        ("I0406", "Ржавая секира",              "greataxe",   "slashing", 0.65,   4,  2, 4,  4, 0, 0, 0, 0, 0, 0.08, 0, "Тяжёлый топор на длинной рукояти."),
        ("I0407", "Железная секира",            "greataxe",   "slashing", 0.65,  12,  5, 8,  8, 1, 0, 0, 0, 0, 0.08, 0, "Железная секира."),
        ("I0408", "Стальная секира",            "greataxe",   "slashing", 0.65,  35,  8, 12, 12, 2, 0, 0, 0, 1, 0.12, 0, "Острая стальная секира."),
        ("I0409", "Эбонитовая секира",          "greataxe",   "slashing", 0.65,  85, 12, 17, 17, 3, 0, 0, 0, 1, 0.18, 0, "Тёмная секира из эбонита."),
        ("I0410", "Мифриловая секира",          "greataxe",   "slashing", 0.65, 250, 19, 27, 27, 5, 0, 0, 0, 2, 0.25, 0, "Легендарная секира."),

        // ===== ДВУРУЧНЫЕ МОЛОТЫ (дробящий, greathammer) =====
        ("I0411", "Ржавый двуручный молот",     "greathammer","blunt",    0.5,    5,  3, 8,  8, 0, 0, 0, 0, 0, 0.1,  0, "Массивный ржавый молот."),
        ("I0412", "Железный двуручный молот",   "greathammer","blunt",    0.5,   15,  6, 12, 12, 2, 1, 0, 0, 0, 0.1,  0, "Железный боевой молот."),
        ("I0413", "Стальной двуручный молот",   "greathammer","blunt",    0.5,   45, 10, 20, 20, 3, 1, 0, 0, 1, 0.15, 0, "Стальной двуручный молот."),
        ("I0414", "Эбонитовый двуручный молот", "greathammer","blunt",    0.5,  100, 15, 29, 29, 5, 2, 0, 0, 1, 0.2,  0, "Тёмный молот из эбонита."),
        ("I0415", "Мифриловый двуручный молот", "greathammer","blunt",    0.5,  300, 22, 40, 40, 7, 3, 0, 0, 2, 0.25, 0, "Легендарный двуручный молот."),

        // ===== АЛЕБАРДЫ (рубящий, halberd) =====
        ("I0416", "Ржавая алебарда",            "halberd",    "slashing", 0.7,    3,  2, 3,  3, 0, 0, 1, 0, 0,   0,    0, "Длинное ржавое копьё с лезвием."),
        ("I0417", "Железная алебарда",          "halberd",    "slashing", 0.7,    9,  5, 7,  7, 1, 0, 1, 0, 0,   0,    0, "Железная алебарда."),
        ("I0418", "Стальная алебарда",          "halberd",    "slashing", 0.7,   28,  8, 11, 11, 2, 0, 1, 0, 1,   0,    0, "Острая стальная алебарда."),
        ("I0419", "Эбонитовая алебарда",        "halberd",    "slashing", 0.7,   65, 11, 16, 16, 3, 0, 2, 0, 1, 0.12,  0, "Тёмная алебарда из эбонита."),
        ("I0420", "Мифриловая алебарда",        "halberd",    "slashing", 0.7,  200, 17, 25, 25, 4, 0, 3, 0, 2, 0.22,  0, "Легендарная алебарда."),

        // ===== КОПЬЯ (колющий, spear) =====
        ("I0421", "Ржавое копьё",               "spear",      "piercing", 0.8,    2,  1, 2,  2, 0, 0, 1, 0, 0.5, 0,    0, "Длинное ржавое копьё."),
        ("I0422", "Железное копьё",             "spear",      "piercing", 0.8,    7,  3, 5,  5, 0, 0, 2, 0, 0.5, 0,    0, "Железное копьё."),
        ("I0423", "Стальное копьё",             "spear",      "piercing", 0.8,   22,  6, 8,  8, 0, 0, 3, 0, 1,   0,    0, "Острое стальное копьё."),
        ("I0424", "Эбонитовое копьё",           "spear",      "piercing", 0.8,   55,  8, 12, 12, 0, 0, 4, 1, 1, 0.05,  0, "Тёмное копьё из эбонита."),
        ("I0425", "Мифриловое копьё",           "spear",      "piercing", 0.8,  170, 13, 18, 18, 0, 0, 6, 2, 2, 0.1,   0, "Легендарное копьё."),
    };

    public override void Up()
    {
        foreach (var w in Weapons)
        {
            string spd = w.speed.ToString("F2", CultureInfo.InvariantCulture);
            string cc = w.critChance.ToString(CultureInfo.InvariantCulture);
            string cd = w.critDmg.ToString(CultureInfo.InvariantCulture);
            string ev = w.evadeChance.ToString(CultureInfo.InvariantCulture);

            Execute.Sql(
                "INSERT INTO items (id, name, type, value, attack, defense, max_health_bonus, heal_amount, stock, description, " +
                "bonus_strength, bonus_endurance, bonus_agility, bonus_cunning, bonus_intellect, bonus_wisdom, " +
                "bonus_crit_chance, bonus_crit_damage, bonus_evade_chance, " +
                "two_handed, damage_type, attack_speed_modifier, weapon_subtype, damage_min, damage_max) " +
                $"VALUES ('{w.id}','{w.name}','twohand',{w.value},{w.physAtk},0,0,0,1,'{w.desc}'," +
                $"{w.str},{w.end},{w.agi},{w.cun},0,0," +
                $"{cc},{cd},{ev}," +
                $"1,'{w.dmgType}',{spd},'{w.subtype}',{w.dmgMin},{w.dmgMax})");
        }

        // Добавляем всё в ассортимент торговца (N0001)
        foreach (var w in Weapons)
            Execute.Sql($"INSERT OR IGNORE INTO merchant_stock (npc_id, item_id) VALUES ('N0001','{w.id}')");
    }
}

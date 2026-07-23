using System.Globalization;
using FluentMigrator;

namespace RPGGame.Shared.Migrations;

/// <summary>
/// Добавляем луки (bows) и посохи (staffs), а также колонку attack_range.
/// 5 луков (I0501–I0505) + 5 посохов (I0506–I0510).
/// Луки: дальность 3, piercing, two-handed, Cunning.
/// Посохи: дальность 2, two-handed, магический урон, Intellect.
/// </summary>
[Migration(23)]
public class AddBowAndStaff : ForwardOnlyMigration
{
    private static readonly (string id, string name, string subtype, string dmgType, double speed,
        int value, int attack, int dmgMin, int dmgMax,
        int magAttack, int str, int end, int agi, int cun, int intel,
        double critChance, double critDmg, double evadeChance,
        string desc)[] Weapons =
    {
        // ===== ЛУКИ (колющий, bow) — дальность 3, Cunning =====
        ("I0501", "Ржавый лук",               "bow",    "piercing", 0.7,    4,  1, 2,  2, 0, 0, 0, 2, 0, 0, 0.5, 0,    0, "Простой ржавый лук."),
        ("I0502", "Железный лук",             "bow",    "piercing", 0.7,   12,  3, 5,  5, 0, 0, 0, 4, 0, 0, 0.5, 0,    0, "Крепкий железный лук."),
        ("I0503", "Стальной лук",             "bow",    "piercing", 0.7,   35,  5, 9,  9, 0, 0, 0, 6, 0, 0, 1.0, 0.1,  0, "Острый стальной лук."),
        ("I0504", "Эбонитовый лук",           "bow",    "piercing", 0.7,   80,  8, 14, 14, 0, 0, 0, 8, 0, 0, 1.5, 0.15, 0, "Тёмный лук из эбонита."),
        ("I0505", "Мифриловый лук",           "bow",    "piercing", 0.7,  250, 12, 22, 22, 0, 0, 0, 10, 1, 0, 2.0, 0.25, 0, "Легендарный мифриловый лук."),

        // ===== ПОСОХИ (magic, staff) — дальность 2, Intellect =====
        ("I0506", "Дубинка ученика",           "staff",  "magic",   0.65,    3,  0, 1,  1, 3, 0, 0, 0, 2, 0,   0,    0, 0, "Простая деревянная палка."),
        ("I0507", "Посох подмастерья",         "staff",  "magic",   0.65,   10,  0, 3,  3, 7, 0, 0, 0, 4, 0,   0,    0, 0, "Посох подмастерья."),
        ("I0508", "Посох мастера",            "staff",  "magic",   0.65,   30,  0, 6,  6, 12, 0, 0, 0, 6, 0,   0, 0.1,  0, "Мощный посох мастера."),
        ("I0509", "Эбонитовый посох",         "staff",  "magic",   0.65,   75,  0, 10, 10, 18, 0, 0, 0, 8, 1, 0, 0.15, 0, "Тёмный посох из эбонита."),
        ("I0510", "Посох архимага",           "staff",  "magic",   0.65,  220,  0, 16, 16, 28, 0, 0, 0, 10, 2, 0, 0.25, 0, "Легендарный посох архимага."),
    };

    public override void Up()
    {
        // Добавляем колонку attack_range в обе таблицы
        Execute.Sql("ALTER TABLE items ADD COLUMN attack_range INTEGER NOT NULL DEFAULT 1");
        Execute.Sql("ALTER TABLE inventory ADD COLUMN attack_range INTEGER NOT NULL DEFAULT 1");

        // Ставим дальность 2 у копий (spear) — остальные уже 1
        Execute.Sql("UPDATE items SET attack_range = 2 WHERE weapon_subtype = 'spear'");

        foreach (var w in Weapons)
        {
            string spd = w.speed.ToString("F2", CultureInfo.InvariantCulture);
            string cc = w.critChance.ToString(CultureInfo.InvariantCulture);
            string cd = w.critDmg.ToString(CultureInfo.InvariantCulture);
            string ev = w.evadeChance.ToString(CultureInfo.InvariantCulture);
            int range = w.subtype == "bow" ? 5 : 4;

            Execute.Sql(
                "INSERT INTO items (id, name, type, value, attack, defense, max_health_bonus, heal_amount, stock, description, " +
                "bonus_strength, bonus_endurance, bonus_agility, bonus_cunning, bonus_intellect, bonus_wisdom, " +
                "bonus_phys_attack, bonus_mag_attack, bonus_resistance, " +
                "bonus_crit_chance, bonus_crit_damage, bonus_evade_chance, " +
                "two_handed, damage_type, attack_speed_modifier, weapon_subtype, damage_min, damage_max, attack_range) " +
                $"VALUES ('{w.id}','{w.name}','twohand',{w.value},{w.attack},0,0,0,1,'{w.desc}'," +
                $"{w.str},{w.end},{w.agi},{w.cun},{w.intel},0," +
                $"0,{w.magAttack},0," +
                $"{cc},{cd},{ev}," +
                $"1,'{w.dmgType}',{spd},'{w.subtype}',{w.dmgMin},{w.dmgMax},{range})");
        }

        // Добавляем всё в ассортимент торговца (N0001)
        foreach (var w in Weapons)
            Execute.Sql($"INSERT OR IGNORE INTO merchant_stock (npc_id, item_id) VALUES ('N0001','{w.id}')");
    }
}

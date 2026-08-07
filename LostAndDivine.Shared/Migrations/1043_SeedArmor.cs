using FluentMigrator;

namespace LostAndDivine.Shared.Migrations;

[Migration(1043)]
public class SeedArmor : ForwardOnlyMigration
{
    public override void Up()
    {
        // Remove legacy single-tier armor that lacks quality
        Execute.Sql("DELETE FROM items WHERE type IN ('chest','helmet','legs','boots','glove','belt','cloak','necklace','ring','accessory','consumable') AND id NOT LIKE 'W%' AND description NOT LIKE 'Качество:%'");
        Execute.Sql("DELETE FROM merchant_stock WHERE item_id NOT IN (SELECT id FROM items)");

        // ===== Armor items (A-prefix) =====
        // chest: (level, baseDef, baseValue)
        SeedSlot("A", new[] { "chest" }, new (string name, int lvl, int def, int val)[] {
            ("Кожаный доспех", 5, 4, 40),
            ("Кольчужная броня", 15, 14, 160),
            ("Стальные латы", 25, 28, 400),
            ("Рыцарский доспех", 35, 48, 900),
            ("Драконья броня", 45, 72, 1700),
        });

        // helmet
        SeedSlot("B", new[] { "helmet" }, new (string name, int lvl, int def, int val)[] {
            ("Кожаный шлем", 5, 2, 20),
            ("Железный шлем", 15, 8, 80),
            ("Стальной шлем", 25, 16, 200),
            ("Шлем рыцаря", 35, 28, 450),
            ("Шлем дракона", 45, 42, 850),
        });

        // legs
        SeedSlot("C", new[] { "legs" }, new (string name, int lvl, int def, int val)[] {
            ("Кожаные поножи", 5, 3, 25),
            ("Кольчужные поножи", 15, 10, 100),
            ("Стальные поножи", 25, 20, 250),
            ("Поножи рыцаря", 35, 34, 550),
            ("Поножи дракона", 45, 52, 1050),
        });

        // boots
        SeedSlot("D", new[] { "boots" }, new (string name, int lvl, int def, int val)[] {
            ("Кожаные сапоги", 5, 2, 20),
            ("Укреплённые сапоги", 15, 6, 70),
            ("Стальные сапоги", 25, 12, 180),
            ("Сапоги рыцаря", 35, 22, 400),
            ("Сапоги дракона", 45, 34, 750),
        });

        // glove
        SeedSlot("E", new[] { "glove" }, new (string name, int lvl, int def, int val)[] {
            ("Кожаные перчатки", 5, 1, 15),
            ("Укреплённые перчатки", 15, 5, 55),
            ("Стальные перчатки", 25, 10, 140),
            ("Перчатки рыцаря", 35, 18, 300),
            ("Перчатки дракона", 45, 28, 600),
        });

        // belt
        SeedSlot("F", new[] { "belt" }, new (string name, int lvl, int def, int val)[] {
            ("Кожаный пояс", 5, 1, 15),
            ("Ремень воина", 15, 4, 50),
            ("Стальной пояс", 25, 9, 130),
            ("Пояс рыцаря", 35, 16, 280),
            ("Пояс дракона", 45, 26, 550),
        });

        // cloak
        SeedSlot("G", new[] { "cloak" }, new (string name, int lvl, int def, int val)[] {
            ("Поношенный плащ", 5, 1, 12),
            ("Шерстяной плащ", 15, 4, 45),
            ("Плащ стража", 25, 8, 110),
            ("Плащ рыцаря", 35, 14, 250),
            ("Плащ дракона", 45, 22, 480),
        });

        // necklace
        SeedSlot("H", new[] { "necklace" }, new (string name, int lvl, int def, int val)[] {
            ("Медное ожерелье", 5, 0, 30),
            ("Серебряное ожерелье", 15, 1, 120),
            ("Ожерелье защиты", 25, 2, 300),
            ("Ожерелье рыцаря", 35, 3, 650),
            ("Ожерелье дракона", 45, 5, 1100),
        });

        // ring
        SeedSlot("I", new[] { "ring" }, new (string name, int lvl, int def, int val)[] {
            ("Медное кольцо", 5, 0, 25),
            ("Серебряное кольцо", 15, 0, 100),
            ("Кольцо защиты", 25, 1, 250),
            ("Кольцо рыцаря", 35, 2, 550),
            ("Кольцо дракона", 45, 3, 950),
        });
    }

    private int _armorSeq;

    private void SeedSlot(string prefix, string[] types, (string name, int lvl, int def, int val)[] tiers)
    {
        foreach (var (name, lvl, def, val) in tiers)
        {
            SeedQualityGroup(prefix, name, types[0], lvl, def, val);
        }
    }

    private void SeedQualityGroup(string prefix, string name, string type, int lvl, int def, int val)
    {
        _armorSeq++;
        InsA($"{prefix}{_armorSeq:D4}", name, type, "Обычный", val, def, lvl, 0);
        _armorSeq++;
        InsA($"{prefix}{_armorSeq:D4}", name, type, "Необычный", val * 23 / 10, (int)(def * 1.3), lvl, 1);
        _armorSeq++;
        InsA($"{prefix}{_armorSeq:D4}", name, type, "Редкий", val * 37 / 10, (int)(def * 1.6), lvl, 2);
        _armorSeq++;
        InsA($"{prefix}{_armorSeq:D4}", name, type, "Эпический", val * 7, def * 2, lvl, 3);
    }

    private void InsA(string id, string name, string type, string qualityLabel, int value, int defense, int requiredLevel, int bonusCount)
    {
        int bonusValue = Math.Max(1, requiredLevel / 5);
        string[] allStats = { "bonus_strength", "bonus_endurance", "bonus_agility", "bonus_cunning", "bonus_intellect", "bonus_wisdom" };
        int itemNum = int.Parse(id.AsSpan(1));
        var bonuses = new int[6];
        for (int t = 0; t < bonusCount; t++)
        {
            int si = (itemNum + t * 2) % 6;
            bonuses[si] = bonusValue;
        }

        Execute.Sql(FormattableString.Invariant(
            $"INSERT INTO items (id,name,type,value,attack,defense,max_health_bonus,heal_amount,restore_mana,stock,description,bonus_strength,bonus_endurance,bonus_agility,bonus_cunning,bonus_intellect,bonus_wisdom,bonus_phys_attack,bonus_mag_attack,bonus_defense,bonus_resistance,bonus_attack_speed,bonus_crit_chance,bonus_crit_damage,bonus_evade_chance,two_handed,damage_type,attack_speed_modifier,weapon_subtype,damage_min,damage_max,attack_range,required_level) VALUES ('{id}','{Esc(name)}','{type}',{value},0,{defense},0,0,0,1,'Качество: {qualityLabel}',{bonuses[0]},{bonuses[1]},{bonuses[2]},{bonuses[3]},{bonuses[4]},{bonuses[5]},0,0,0,0,0.0,0.0,0.0,0.0,0,'',1.0,'',0,0,1,{requiredLevel})"));
    }

    private static string Esc(string s) => s.Replace("'", "''");
}

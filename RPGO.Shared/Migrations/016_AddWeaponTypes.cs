using FluentMigrator;

namespace RPGGame.Shared.Migrations;

/// <summary>
/// Добавляем новые типы оружия: топоры, булавы, молоты, кинжалы.
/// Обновляем существующие мечи — проставляем damage_type и attack_speed_modifier.
/// Добавляем всё в ассортимент торговца (N0001).
/// </summary>
[Migration(16)]
public class AddWeaponTypes : ForwardOnlyMigration
{
    public override void Up()
    {
        // --- Обновляем существующие мечи (I0001-I0005) ---
        Execute.Sql("UPDATE items SET damage_type = 'slashing', attack_speed_modifier = 1.0 WHERE id = 'I0001'");
        Execute.Sql("UPDATE items SET damage_type = 'slashing', attack_speed_modifier = 1.0 WHERE id = 'I0002'");
        Execute.Sql("UPDATE items SET damage_type = 'slashing', attack_speed_modifier = 1.0 WHERE id = 'I0003'");
        Execute.Sql("UPDATE items SET damage_type = 'slashing', attack_speed_modifier = 1.0 WHERE id = 'I0004'");
        Execute.Sql("UPDATE items SET damage_type = 'slashing', attack_speed_modifier = 1.0 WHERE id = 'I0005'");

        // --- Топоры (рубящий, модификатор 0.8) ---
        // I0301 - I0305
        Execute.Sql("INSERT INTO items (id, name, type, value, attack, defense, max_health_bonus, heal_amount, stock, description, bonus_strength, bonus_stamina, bonus_agility, bonus_cunning, bonus_wisdom, bonus_will, bonus_crit_chance, bonus_crit_damage, bonus_evade_chance, two_handed, damage_type, attack_speed_modifier) " +
            "VALUES ('I0301','Ржавый топор','weapon',2,1,0,0,0,1,'Тяжёлый, но рабочий.',0,0,0,0,0,0,0,0.05,0,0,'slashing',0.8)");
        Execute.Sql("INSERT INTO items (id, name, type, value, attack, defense, max_health_bonus, heal_amount, stock, description, bonus_strength, bonus_stamina, bonus_agility, bonus_cunning, bonus_wisdom, bonus_will, bonus_crit_chance, bonus_crit_damage, bonus_evade_chance, two_handed, damage_type, attack_speed_modifier) " +
            "VALUES ('I0302','Железный топор','weapon',8,3,0,0,0,1,'Крепкий топор.',1,0,0,0,0,0,0,0.05,0,0,'slashing',0.8)");
        Execute.Sql("INSERT INTO items (id, name, type, value, attack, defense, max_health_bonus, heal_amount, stock, description, bonus_strength, bonus_stamina, bonus_agility, bonus_cunning, bonus_wisdom, bonus_will, bonus_crit_chance, bonus_crit_damage, bonus_evade_chance, two_handed, damage_type, attack_speed_modifier) " +
            "VALUES ('I0303','Стальной топор','weapon',25,5,0,0,0,1,'Острый стальной топор.',2,0,0,0,0,0,1,0.1,0,0,'slashing',0.8)");
        Execute.Sql("INSERT INTO items (id, name, type, value, attack, defense, max_health_bonus, heal_amount, stock, description, bonus_strength, bonus_stamina, bonus_agility, bonus_cunning, bonus_wisdom, bonus_will, bonus_crit_chance, bonus_crit_damage, bonus_evade_chance, two_handed, damage_type, attack_speed_modifier) " +
            "VALUES ('I0304','Эбонитовый топор','weapon',60,7,0,0,0,1,'Тёмный топор из эбонита.',3,0,0,0,0,0,1,0.15,0,0,'slashing',0.8)");
        Execute.Sql("INSERT INTO items (id, name, type, value, attack, defense, max_health_bonus, heal_amount, stock, description, bonus_strength, bonus_stamina, bonus_agility, bonus_cunning, bonus_wisdom, bonus_will, bonus_crit_chance, bonus_crit_damage, bonus_evade_chance, two_handed, damage_type, attack_speed_modifier) " +
            "VALUES ('I0305','Мифриловый топор','weapon',180,12,0,0,0,1,'Легендарный топор.',5,1,0,0,0,0,2,0.2,0,0,'slashing',0.8)");

        // --- Булавы (дробящий, модификатор 0.6) ---
        // I0306 - I0310
        Execute.Sql("INSERT INTO items (id, name, type, value, attack, defense, max_health_bonus, heal_amount, stock, description, bonus_strength, bonus_stamina, bonus_agility, bonus_cunning, bonus_wisdom, bonus_will, bonus_crit_chance, bonus_crit_damage, bonus_evade_chance, two_handed, damage_type, attack_speed_modifier) " +
            "VALUES ('I0306','Ржавая булава','weapon',2,2,0,0,0,1,'Примитивная, но тяжёлая.',0,0,0,0,0,0,0,0.05,0,0,'blunt',0.6)");
        Execute.Sql("INSERT INTO items (id, name, type, value, attack, defense, max_health_bonus, heal_amount, stock, description, bonus_strength, bonus_stamina, bonus_agility, bonus_cunning, bonus_wisdom, bonus_will, bonus_crit_chance, bonus_crit_damage, bonus_evade_chance, two_handed, damage_type, attack_speed_modifier) " +
            "VALUES ('I0307','Железная булава','weapon',10,4,0,0,0,1,'Массивная железная булава.',1,1,0,0,0,0,0,0.05,0,0,'blunt',0.6)");
        Execute.Sql("INSERT INTO items (id, name, type, value, attack, defense, max_health_bonus, heal_amount, stock, description, bonus_strength, bonus_stamina, bonus_agility, bonus_cunning, bonus_wisdom, bonus_will, bonus_crit_chance, bonus_crit_damage, bonus_evade_chance, two_handed, damage_type, attack_speed_modifier) " +
            "VALUES ('I0308','Стальная булава','weapon',30,7,0,0,0,1,'Стальная булава с шипами.',2,1,0,0,0,0,1,0.1,0,0,'blunt',0.6)");
        Execute.Sql("INSERT INTO items (id, name, type, value, attack, defense, max_health_bonus, heal_amount, stock, description, bonus_strength, bonus_stamina, bonus_agility, bonus_cunning, bonus_wisdom, bonus_will, bonus_crit_chance, bonus_crit_damage, bonus_evade_chance, two_handed, damage_type, attack_speed_modifier) " +
            "VALUES ('I0309','Эбонитовая булава','weapon',70,10,0,0,0,1,'Тёмная булава из эбонита.',4,1,0,0,0,0,1,0.15,0,0,'blunt',0.6)");
        Execute.Sql("INSERT INTO items (id, name, type, value, attack, defense, max_health_bonus, heal_amount, stock, description, bonus_strength, bonus_stamina, bonus_agility, bonus_cunning, bonus_wisdom, bonus_will, bonus_crit_chance, bonus_crit_damage, bonus_evade_chance, two_handed, damage_type, attack_speed_modifier) " +
            "VALUES ('I0310','Мифриловая булава','weapon',200,16,0,0,0,1,'Легендарная булава.',6,2,0,0,0,0,2,0.2,0,0,'blunt',0.6)");

        // --- Молоты (дробящий, модификатор 0.5) ---
        // I0311 - I0315
        Execute.Sql("INSERT INTO items (id, name, type, value, attack, defense, max_health_bonus, heal_amount, stock, description, bonus_strength, bonus_stamina, bonus_agility, bonus_cunning, bonus_wisdom, bonus_will, bonus_crit_chance, bonus_crit_damage, bonus_evade_chance, two_handed, damage_type, attack_speed_modifier) " +
            "VALUES ('I0311','Ржавый молот','weapon',3,3,0,0,0,1,'Тяжёлый и неуклюжий.',0,0,0,0,0,0,0,0.1,0,0,'blunt',0.5)");
        Execute.Sql("INSERT INTO items (id, name, type, value, attack, defense, max_health_bonus, heal_amount, stock, description, bonus_strength, bonus_stamina, bonus_agility, bonus_cunning, bonus_wisdom, bonus_will, bonus_crit_chance, bonus_crit_damage, bonus_evade_chance, two_handed, damage_type, attack_speed_modifier) " +
            "VALUES ('I0312','Железный молот','weapon',12,5,0,0,0,1,'Крепкий боевой молот.',2,1,0,0,0,0,0,0.1,0,0,'blunt',0.5)");
        Execute.Sql("INSERT INTO items (id, name, type, value, attack, defense, max_health_bonus, heal_amount, stock, description, bonus_strength, bonus_stamina, bonus_agility, bonus_cunning, bonus_wisdom, bonus_will, bonus_crit_chance, bonus_crit_damage, bonus_evade_chance, two_handed, damage_type, attack_speed_modifier) " +
            "VALUES ('I0313','Стальной молот','weapon',35,8,0,0,0,1,'Молот из закалённой стали.',3,1,0,0,0,0,1,0.15,0,0,'blunt',0.5)");
        Execute.Sql("INSERT INTO items (id, name, type, value, attack, defense, max_health_bonus, heal_amount, stock, description, bonus_strength, bonus_stamina, bonus_agility, bonus_cunning, bonus_wisdom, bonus_will, bonus_crit_chance, bonus_crit_damage, bonus_evade_chance, two_handed, damage_type, attack_speed_modifier) " +
            "VALUES ('I0314','Эбонитовый молот','weapon',80,12,0,0,0,1,'Тёмный молот из эбонита.',5,2,0,0,0,0,1,0.2,0,0,'blunt',0.5)");
        Execute.Sql("INSERT INTO items (id, name, type, value, attack, defense, max_health_bonus, heal_amount, stock, description, bonus_strength, bonus_stamina, bonus_agility, bonus_cunning, bonus_wisdom, bonus_will, bonus_crit_chance, bonus_crit_damage, bonus_evade_chance, two_handed, damage_type, attack_speed_modifier) " +
            "VALUES ('I0315','Мифриловый молот','weapon',220,18,0,0,0,1,'Легендарный боевой молот.',7,3,0,0,0,0,2,0.25,0,0,'blunt',0.5)");

        // --- Кинжалы (колющий, модификатор 1.3) ---
        // I0316 - I0320
        Execute.Sql("INSERT INTO items (id, name, type, value, attack, defense, max_health_bonus, heal_amount, stock, description, bonus_strength, bonus_stamina, bonus_agility, bonus_cunning, bonus_wisdom, bonus_will, bonus_crit_chance, bonus_crit_damage, bonus_evade_chance, two_handed, damage_type, attack_speed_modifier) " +
            "VALUES ('I0316','Ржавый кинжал','weapon',1,1,0,0,0,1,'Быстрый, но слабый.',0,0,1,0,0,0,1,0,1,0,'piercing',1.3)");
        Execute.Sql("INSERT INTO items (id, name, type, value, attack, defense, max_health_bonus, heal_amount, stock, description, bonus_strength, bonus_stamina, bonus_agility, bonus_cunning, bonus_wisdom, bonus_will, bonus_crit_chance, bonus_crit_damage, bonus_evade_chance, two_handed, damage_type, attack_speed_modifier) " +
            "VALUES ('I0317','Железный кинжал','weapon',4,2,0,0,0,1,'Острый железный кинжал.',0,0,2,0,0,0,1,0,2,0,'piercing',1.3)");
        Execute.Sql("INSERT INTO items (id, name, type, value, attack, defense, max_health_bonus, heal_amount, stock, description, bonus_strength, bonus_stamina, bonus_agility, bonus_cunning, bonus_wisdom, bonus_will, bonus_crit_chance, bonus_crit_damage, bonus_evade_chance, two_handed, damage_type, attack_speed_modifier) " +
            "VALUES ('I0318','Стальной кинжал','weapon',18,4,0,0,0,1,'Стальной кинжал убийцы.',0,0,3,0,0,0,2,0,3,0,'piercing',1.3)");
        Execute.Sql("INSERT INTO items (id, name, type, value, attack, defense, max_health_bonus, heal_amount, stock, description, bonus_strength, bonus_stamina, bonus_agility, bonus_cunning, bonus_wisdom, bonus_will, bonus_crit_chance, bonus_crit_damage, bonus_evade_chance, two_handed, damage_type, attack_speed_modifier) " +
            "VALUES ('I0319','Эбонитовый кинжал','weapon',45,6,0,0,0,1,'Тёмный кинжал из эбонита.',0,0,4,1,0,0,2,0.05,4,0,'piercing',1.3)");
        Execute.Sql("INSERT INTO items (id, name, type, value, attack, defense, max_health_bonus, heal_amount, stock, description, bonus_strength, bonus_stamina, bonus_agility, bonus_cunning, bonus_wisdom, bonus_will, bonus_crit_chance, bonus_crit_damage, bonus_evade_chance, two_handed, damage_type, attack_speed_modifier) " +
            "VALUES ('I0320','Мифриловый кинжал','weapon',140,9,0,0,0,1,'Легендарный кинжал.',0,0,6,2,0,0,3,0.1,6,0,'piercing',1.3)");

        // ===== Ассортимент торговца (N0001) =====
        // Существующее оружие (мечи)
        Execute.Sql("INSERT OR IGNORE INTO merchant_stock (npc_id, item_id) VALUES ('N0001','I0001')");
        Execute.Sql("INSERT OR IGNORE INTO merchant_stock (npc_id, item_id) VALUES ('N0001','I0002')");
        Execute.Sql("INSERT OR IGNORE INTO merchant_stock (npc_id, item_id) VALUES ('N0001','I0003')");
        Execute.Sql("INSERT OR IGNORE INTO merchant_stock (npc_id, item_id) VALUES ('N0001','I0004')");
        Execute.Sql("INSERT OR IGNORE INTO merchant_stock (npc_id, item_id) VALUES ('N0001','I0005')");

        // Топоры
        Execute.Sql("INSERT OR IGNORE INTO merchant_stock (npc_id, item_id) VALUES ('N0001','I0301')");
        Execute.Sql("INSERT OR IGNORE INTO merchant_stock (npc_id, item_id) VALUES ('N0001','I0302')");
        Execute.Sql("INSERT OR IGNORE INTO merchant_stock (npc_id, item_id) VALUES ('N0001','I0303')");
        Execute.Sql("INSERT OR IGNORE INTO merchant_stock (npc_id, item_id) VALUES ('N0001','I0304')");
        Execute.Sql("INSERT OR IGNORE INTO merchant_stock (npc_id, item_id) VALUES ('N0001','I0305')");

        // Булавы
        Execute.Sql("INSERT OR IGNORE INTO merchant_stock (npc_id, item_id) VALUES ('N0001','I0306')");
        Execute.Sql("INSERT OR IGNORE INTO merchant_stock (npc_id, item_id) VALUES ('N0001','I0307')");
        Execute.Sql("INSERT OR IGNORE INTO merchant_stock (npc_id, item_id) VALUES ('N0001','I0308')");
        Execute.Sql("INSERT OR IGNORE INTO merchant_stock (npc_id, item_id) VALUES ('N0001','I0309')");
        Execute.Sql("INSERT OR IGNORE INTO merchant_stock (npc_id, item_id) VALUES ('N0001','I0310')");

        // Молоты
        Execute.Sql("INSERT OR IGNORE INTO merchant_stock (npc_id, item_id) VALUES ('N0001','I0311')");
        Execute.Sql("INSERT OR IGNORE INTO merchant_stock (npc_id, item_id) VALUES ('N0001','I0312')");
        Execute.Sql("INSERT OR IGNORE INTO merchant_stock (npc_id, item_id) VALUES ('N0001','I0313')");
        Execute.Sql("INSERT OR IGNORE INTO merchant_stock (npc_id, item_id) VALUES ('N0001','I0314')");
        Execute.Sql("INSERT OR IGNORE INTO merchant_stock (npc_id, item_id) VALUES ('N0001','I0315')");

        // Кинжалы
        Execute.Sql("INSERT OR IGNORE INTO merchant_stock (npc_id, item_id) VALUES ('N0001','I0316')");
        Execute.Sql("INSERT OR IGNORE INTO merchant_stock (npc_id, item_id) VALUES ('N0001','I0317')");
        Execute.Sql("INSERT OR IGNORE INTO merchant_stock (npc_id, item_id) VALUES ('N0001','I0318')");
        Execute.Sql("INSERT OR IGNORE INTO merchant_stock (npc_id, item_id) VALUES ('N0001','I0319')");
        Execute.Sql("INSERT OR IGNORE INTO merchant_stock (npc_id, item_id) VALUES ('N0001','I0320')");
    }
}

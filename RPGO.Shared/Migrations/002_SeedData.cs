using FluentMigrator;

namespace RPGGame.Shared.Migrations;

[Migration(2)]
public class SeedData : ForwardOnlyMigration
{
    public override void Up()
    {
        // ===== Предметы =====
        Insert.IntoTable("items").Row(new { id = "I0001", name = "Ржавый меч", type = "weapon", value = 1, attack = 1, defense = 0, max_health_bonus = 0, heal_amount = 0, stock = 1, description = "Покрытый ржавчиной, но ещё работает" });
        Insert.IntoTable("items").Row(new { id = "I0002", name = "Железный меч", type = "weapon", value = 5, attack = 2, defense = 0, max_health_bonus = 0, heal_amount = 0, stock = 1, description = "Надёжный железный меч" });
        Insert.IntoTable("items").Row(new { id = "I0003", name = "Стальной меч", type = "weapon", value = 20, attack = 3, defense = 0, max_health_bonus = 0, heal_amount = 0, stock = 1, description = "Острый стальной клинок" });
        Insert.IntoTable("items").Row(new { id = "I0004", name = "Эбонитовый меч", type = "weapon", value = 50, attack = 5, defense = 0, max_health_bonus = 0, heal_amount = 0, stock = 1, description = "Тёмный меч из эбонита", bonus_strength = 1, bonus_agility = 1, bonus_crit_damage = 0.1 });
        Insert.IntoTable("items").Row(new { id = "I0005", name = "Мифриловый меч", type = "weapon", value = 150, attack = 10, defense = 0, max_health_bonus = 0, heal_amount = 0, stock = 1, description = "Легендарный меч из мифрила", bonus_strength = 3, bonus_agility = 2, bonus_crit_chance = 2.0, bonus_crit_damage = 0.3 });

        Insert.IntoTable("items").Row(new { id = "I0006", name = "Ржавая кираса", type = "armor", value = 3, attack = 0, defense = 1, max_health_bonus = 0, heal_amount = 0, stock = 1, description = "Потрёпанная, но кое-как защищает" });
        Insert.IntoTable("items").Row(new { id = "I0007", name = "Железная броня", type = "armor", value = 10, attack = 0, defense = 3, max_health_bonus = 0, heal_amount = 0, stock = 1, description = "Крепкая железная броня", bonus_stamina = 1 });
        Insert.IntoTable("items").Row(new { id = "I0008", name = "Стальная броня", type = "armor", value = 25, attack = 0, defense = 7, max_health_bonus = 0, heal_amount = 0, stock = 1, description = "Прочная стальная броня", bonus_stamina = 2 });
        Insert.IntoTable("items").Row(new { id = "I0009", name = "Эбонитовая броня", type = "armor", value = 100, attack = 0, defense = 15, max_health_bonus = 0, heal_amount = 0, stock = 1, description = "Тёмная броня из эбонита", bonus_stamina = 4, bonus_will = 1, bonus_evade_chance = 2.0 });
        Insert.IntoTable("items").Row(new { id = "I0010", name = "Мифриловая броня", type = "armor", value = 450, attack = 0, defense = 30, max_health_bonus = 0, heal_amount = 0, stock = 1, description = "Легендарная броня из мифрила", bonus_stamina = 8, bonus_will = 3, bonus_evade_chance = 5.0 });

        Insert.IntoTable("items").Row(new { id = "I0011", name = "Железное кольцо", type = "accessory", value = 30, attack = 2, defense = 0, max_health_bonus = 0, heal_amount = 0, stock = 1, description = "Простое железное кольцо" });
        Insert.IntoTable("items").Row(new { id = "I0012", name = "Кольцо жизни", type = "accessory", value = 50, attack = 0, defense = 0, max_health_bonus = 20, heal_amount = 0, stock = 1, description = "+20 к максимальному здоровью", bonus_stamina = 2 });
        Insert.IntoTable("items").Row(new { id = "I0013", name = "Изумрудное кольцо", type = "accessory", value = 100, attack = 5, defense = 5, max_health_bonus = 5, heal_amount = 0, stock = 1, description = "+5 атаки, +5 защиты, +5 HP", bonus_strength = 1, bonus_stamina = 1, bonus_agility = 1, bonus_crit_chance = 1.0, bonus_crit_damage = 0.1, bonus_evade_chance = 1.0 });

        Insert.IntoTable("items").Row(new { id = "I0014", name = "Зелье здоровья", type = "consumable", value = 20, attack = 0, defense = 0, max_health_bonus = 0, heal_amount = 50, stock = 99, description = "Восстанавливает 50 HP" });
        Insert.IntoTable("items").Row(new { id = "I0015", name = "Ягоды", type = "collectible", value = 1, attack = 0, defense = 0, max_health_bonus = 0, heal_amount = 0, stock = 99, description = "Сочные ягоды, годятся для квестов" });
        Insert.IntoTable("items").Row(new { id = "I0016", name = "Грибы", type = "collectible", value = 1, attack = 0, defense = 0, max_health_bonus = 0, heal_amount = 0, stock = 99, description = "Свежие грибы, годятся для квестов" });
        Insert.IntoTable("items").Row(new { id = "I0017", name = "Мёд", type = "collectible", value = 2, attack = 0, defense = 0, max_health_bonus = 0, heal_amount = 0, stock = 99, description = "Сладкий мёд, годится для квестов" });
        Insert.IntoTable("items").Row(new { id = "I0018", name = "Трава", type = "collectible", value = 1, attack = 0, defense = 0, max_health_bonus = 0, heal_amount = 0, stock = 99, description = "Простая трава. Может пригодиться алхимику." });
        Insert.IntoTable("items").Row(new { id = "I0019", name = "Руда", type = "collectible", value = 3, attack = 0, defense = 0, max_health_bonus = 0, heal_amount = 0, stock = 99, description = "Самородная руда. Ценный материал." });

        // ===== Навыки =====
        Insert.IntoTable("skills").Row(new { id = "SK0001", name = "Сильный удар", description = "Мощный удар, наносящий удвоенный урон цели.", type = "active", mp_cost = 0, cooldown_ms = 3000, damage_multiplier = 2.0, min_level = 1 });

        // ===== Квесты =====
        Insert.IntoTable("quests_def").Row(new { id = "Q0001", title = "Истребление крыс", description = "Убейте 5 крыс рядом с торговцем.", type = "kill", target_monster_id = "M0001", target = 5, xp_reward = 30, gold_reward = 20 });
        Insert.IntoTable("quests_def").Row(new { id = "Q0002", title = "Волчья стая", description = "Убейте 3 волка.", type = "kill", target_monster_id = "M0006", target = 3, xp_reward = 60, gold_reward = 50 });
        Insert.IntoTable("quests_def").Row(new { id = "Q0003", title = "Драконоборец", description = "Убейте 2 дракона.", type = "kill", target_monster_id = "M0011", target = 2, xp_reward = 200, gold_reward = 150 });
        Insert.IntoTable("quests_def").Row(new { id = "Q0004", title = "Сбор ягод", description = "Соберите 8 ягод с кустов.", type = "collect", target_item_id = "I0015", target = 8, xp_reward = 25, gold_reward = 15 });
        Insert.IntoTable("quests_def").Row(new { id = "Q0005", title = "Грибная охота", description = "Соберите 6 грибов.", type = "collect", target_item_id = "I0016", target = 6, xp_reward = 30, gold_reward = 20 });
        Insert.IntoTable("quests_def").Row(new { id = "Q0006", title = "Медовый сезон", description = "Соберите 4 мёда из ульев.", type = "collect", target_item_id = "I0017", target = 4, xp_reward = 50, gold_reward = 35 });

        // ===== Мир =====
        Insert.IntoTable("world_config").Row(new { key = "width", value = 100 });
        Insert.IntoTable("world_config").Row(new { key = "height", value = 100 });
        Insert.IntoTable("world_config").Row(new { key = "merchant_x", value = 50 });
        Insert.IntoTable("world_config").Row(new { key = "merchant_y", value = 50 });
        Insert.IntoTable("world_config").Row(new { key = "board_x", value = 48 });
        Insert.IntoTable("world_config").Row(new { key = "board_y", value = 48 });

        // ===== NPC =====
        Insert.IntoTable("npcs").Row(new { id = "N0001", name = "Торговец", type = "merchant", x = 50, y = 50, data = (string?)null });
        Insert.IntoTable("npcs").Row(new { id = "N0002", name = "Доска заданий", type = "board", x = 48, y = 48, data = (string?)null });

        // ===== Монстры (базовые статы, атрибуты пересчитываются отдельно) =====
        Insert.IntoTable("monsters").Row(new { id = "M0001", name = "Крыса", tier = 1, health = 15, attack = 3, defense = 1, xp_reward = 5, gold_reward = 2, symbol = "r", strength = 1, stamina = 1, agility = 2, cunning = 1, wisdom = 1, will = 1 });
        Insert.IntoTable("monsters").Row(new { id = "M0002", name = "Паук", tier = 1, health = 25, attack = 4, defense = 1, xp_reward = 8, gold_reward = 3, symbol = "s", strength = 1, stamina = 1, agility = 3, cunning = 1, wisdom = 1, will = 1 });
        Insert.IntoTable("monsters").Row(new { id = "M0003", name = "Зомби", tier = 1, health = 30, attack = 5, defense = 3, xp_reward = 10, gold_reward = 5, symbol = "Z", strength = 2, stamina = 2, agility = 1, cunning = 1, wisdom = 1, will = 1 });
        Insert.IntoTable("monsters").Row(new { id = "M0004", name = "Гоблин", tier = 2, health = 40, attack = 6, defense = 2, xp_reward = 15, gold_reward = 8, symbol = "g", strength = 2, stamina = 1, agility = 4, cunning = 1, wisdom = 1, will = 1 });
        Insert.IntoTable("monsters").Row(new { id = "M0005", name = "Скелет", tier = 2, health = 45, attack = 7, defense = 3, xp_reward = 18, gold_reward = 10, symbol = "S", strength = 3, stamina = 2, agility = 3, cunning = 1, wisdom = 1, will = 1 });
        Insert.IntoTable("monsters").Row(new { id = "M0006", name = "Волк", tier = 2, health = 55, attack = 9, defense = 3, xp_reward = 22, gold_reward = 12, symbol = "w", strength = 3, stamina = 2, agility = 5, cunning = 1, wisdom = 1, will = 1 });
        Insert.IntoTable("monsters").Row(new { id = "M0007", name = "Медведь", tier = 3, health = 80, attack = 12, defense = 5, xp_reward = 40, gold_reward = 25, symbol = "B", strength = 5, stamina = 3, agility = 3, cunning = 1, wisdom = 1, will = 1 });
        Insert.IntoTable("monsters").Row(new { id = "M0008", name = "Орк", tier = 3, health = 70, attack = 11, defense = 5, xp_reward = 35, gold_reward = 20, symbol = "O", strength = 4, stamina = 3, agility = 3, cunning = 1, wisdom = 1, will = 1 });
        Insert.IntoTable("monsters").Row(new { id = "M0009", name = "Тёмный маг", tier = 3, health = 60, attack = 15, defense = 4, xp_reward = 45, gold_reward = 30, symbol = "M", strength = 3, stamina = 2, agility = 3, cunning = 1, wisdom = 5, will = 3 });
        Insert.IntoTable("monsters").Row(new { id = "M0010", name = "Дракончик", tier = 4, health = 150, attack = 20, defense = 8, xp_reward = 80, gold_reward = 50, symbol = "D", strength = 6, stamina = 4, agility = 4, cunning = 2, wisdom = 3, will = 2 });
        Insert.IntoTable("monsters").Row(new { id = "M0011", name = "Дракон", tier = 4, health = 250, attack = 30, defense = 12, xp_reward = 150, gold_reward = 100, symbol = "D", strength = 8, stamina = 5, agility = 4, cunning = 3, wisdom = 4, will = 3 });
        Insert.IntoTable("monsters").Row(new { id = "M0012", name = "Лич", tier = 4, health = 200, attack = 25, defense = 10, xp_reward = 120, gold_reward = 80, symbol = "M", strength = 5, stamina = 4, agility = 3, cunning = 3, wisdom = 8, will = 5 });
    }
}

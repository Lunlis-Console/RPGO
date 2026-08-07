using FluentMigrator;

namespace LostAndDivine.Shared.Migrations;

[Migration(1000)]
public class ConsolidatedSchema : ForwardOnlyMigration
{
    private const string ItemCols =
        "id, name, type, value, attack, defense, max_health_bonus, heal_amount, restore_mana, stock, description, " +
        "bonus_strength, bonus_endurance, bonus_agility, bonus_cunning, bonus_intellect, bonus_wisdom, " +
        "bonus_phys_attack, bonus_mag_attack, bonus_defense, bonus_resistance, bonus_attack_speed, " +
        "bonus_crit_chance, bonus_crit_damage, bonus_evade_chance, " +
        "two_handed, damage_type, attack_speed_modifier, weapon_subtype, " +
        "damage_min, damage_max, attack_range";

    public override void Up()
    {
        // ── Drop all tables ──
        Execute.Sql("DROP TABLE IF EXISTS world_portals");
        Execute.Sql("DROP TABLE IF EXISTS zones");
        Execute.Sql("DROP TABLE IF EXISTS merchant_stock");
        Execute.Sql("DROP TABLE IF EXISTS loot_tables");
        Execute.Sql("DROP TABLE IF EXISTS friends");
        Execute.Sql("DROP TABLE IF EXISTS player_equipment");
        Execute.Sql("DROP TABLE IF EXISTS equipment_slots");
        Execute.Sql("DROP TABLE IF EXISTS quests");
        Execute.Sql("DROP TABLE IF EXISTS inventory");
        Execute.Sql("DROP TABLE IF EXISTS skills");
        Execute.Sql("DROP TABLE IF EXISTS npcs");
        Execute.Sql("DROP TABLE IF EXISTS world_config");
        Execute.Sql("DROP TABLE IF EXISTS quests_def");
        Execute.Sql("DROP TABLE IF EXISTS monsters");
        Execute.Sql("DROP TABLE IF EXISTS items");
        Execute.Sql("DROP TABLE IF EXISTS accounts");

        // ── accounts ──
        Create.Table("accounts")
            .WithColumn("login").AsString().NotNullable().PrimaryKey()
            .WithColumn("password_hash").AsString().NotNullable()
            .WithColumn("player_name").AsString().NotNullable().Unique()
            .WithColumn("level").AsInt32().WithDefaultValue(1)
            .WithColumn("experience").AsInt32().WithDefaultValue(0)
            .WithColumn("health").AsInt32().WithDefaultValue(100)
            .WithColumn("max_health").AsInt32().WithDefaultValue(100)
            .WithColumn("phys_attack").AsInt32().WithDefaultValue(0)
            .WithColumn("phys_defense").AsInt32().WithDefaultValue(0)
            .WithColumn("gold").AsInt32().WithDefaultValue(0)
            .WithColumn("created_at").AsString().NotNullable()
            .WithColumn("last_login").AsString().NotNullable()
            .WithColumn("weapon_id").AsString().WithDefaultValue("")
            .WithColumn("armor_id").AsString().WithDefaultValue("")
            .WithColumn("accessory_id").AsString().WithDefaultValue("")
            .WithColumn("strength").AsInt32().WithDefaultValue(1)
            .WithColumn("endurance").AsInt32().WithDefaultValue(1)
            .WithColumn("agility").AsInt32().WithDefaultValue(1)
            .WithColumn("cunning").AsInt32().WithDefaultValue(1)
            .WithColumn("intellect").AsInt32().WithDefaultValue(1)
            .WithColumn("wisdom").AsInt32().WithDefaultValue(1)
            .WithColumn("attribute_points").AsInt32().WithDefaultValue(0)
            .WithColumn("speed").AsInt32().WithDefaultValue(1)
            .WithColumn("pos_x").AsInt32().WithDefaultValue(-1)
            .WithColumn("pos_y").AsInt32().WithDefaultValue(-1)
            .WithColumn("hotbar_slots").AsString().WithDefaultValue("")
            .WithColumn("is_admin").AsInt32().WithDefaultValue(0)
            .WithColumn("is_banned").AsInt32().WithDefaultValue(0)
            .WithColumn("ban_reason").AsString().WithDefaultValue("")
            .WithColumn("skill_points").AsInt32().WithDefaultValue(0)
            .WithColumn("learned_skills").AsString().WithDefaultValue("[]")
            .WithColumn("current_zone").AsString().WithDefaultValue("main");

        // ── items ──
        Create.Table("items")
            .WithColumn("id").AsString().NotNullable().PrimaryKey()
            .WithColumn("name").AsString().NotNullable()
            .WithColumn("type").AsString().NotNullable()
            .WithColumn("value").AsInt32().WithDefaultValue(0)
            .WithColumn("attack").AsInt32().WithDefaultValue(0)
            .WithColumn("defense").AsInt32().WithDefaultValue(0)
            .WithColumn("max_health_bonus").AsInt32().WithDefaultValue(0)
            .WithColumn("heal_amount").AsInt32().WithDefaultValue(0)
            .WithColumn("restore_mana").AsInt32().WithDefaultValue(0)
            .WithColumn("stock").AsInt32().WithDefaultValue(1)
            .WithColumn("description").AsString().WithDefaultValue("")
            .WithColumn("bonus_strength").AsInt32().WithDefaultValue(0)
            .WithColumn("bonus_endurance").AsInt32().WithDefaultValue(0)
            .WithColumn("bonus_agility").AsInt32().WithDefaultValue(0)
            .WithColumn("bonus_cunning").AsInt32().WithDefaultValue(0)
            .WithColumn("bonus_intellect").AsInt32().WithDefaultValue(0)
            .WithColumn("bonus_wisdom").AsInt32().WithDefaultValue(0)
            .WithColumn("bonus_phys_attack").AsInt32().WithDefaultValue(0)
            .WithColumn("bonus_mag_attack").AsInt32().WithDefaultValue(0)
            .WithColumn("bonus_defense").AsInt32().WithDefaultValue(0)
            .WithColumn("bonus_resistance").AsInt32().WithDefaultValue(0)
            .WithColumn("bonus_attack_speed").AsDouble().WithDefaultValue(0.0)
            .WithColumn("bonus_crit_chance").AsDouble().WithDefaultValue(0.0)
            .WithColumn("bonus_crit_damage").AsDouble().WithDefaultValue(0.0)
            .WithColumn("bonus_evade_chance").AsDouble().WithDefaultValue(0.0)
            .WithColumn("two_handed").AsInt32().WithDefaultValue(0)
            .WithColumn("damage_type").AsString().WithDefaultValue("")
            .WithColumn("attack_speed_modifier").AsDouble().WithDefaultValue(1.0)
            .WithColumn("weapon_subtype").AsString().WithDefaultValue("")
            .WithColumn("damage_min").AsInt32().WithDefaultValue(0)
            .WithColumn("damage_max").AsInt32().WithDefaultValue(0)
            .WithColumn("attack_range").AsInt32().WithDefaultValue(1);

        // ── inventory ──
        Create.Table("inventory")
            .WithColumn("id").AsInt32().PrimaryKey().Identity()
            .WithColumn("player_name").AsString().NotNullable()
            .WithColumn("item_id").AsString().NotNullable()
            .WithColumn("name").AsString().NotNullable()
            .WithColumn("type").AsString().NotNullable()
            .WithColumn("value").AsInt32().WithDefaultValue(0)
            .WithColumn("defense").AsInt32().WithDefaultValue(0)
            .WithColumn("max_health_bonus").AsInt32().WithDefaultValue(0)
            .WithColumn("heal_amount").AsInt32().WithDefaultValue(0)
            .WithColumn("restore_mana").AsInt32().WithDefaultValue(0)
            .WithColumn("description").AsString().WithDefaultValue("")
            .WithColumn("bonus_strength").AsInt32().WithDefaultValue(0)
            .WithColumn("bonus_endurance").AsInt32().WithDefaultValue(0)
            .WithColumn("bonus_agility").AsInt32().WithDefaultValue(0)
            .WithColumn("bonus_cunning").AsInt32().WithDefaultValue(0)
            .WithColumn("bonus_intellect").AsInt32().WithDefaultValue(0)
            .WithColumn("bonus_wisdom").AsInt32().WithDefaultValue(0)
            .WithColumn("bonus_phys_attack").AsInt32().WithDefaultValue(0)
            .WithColumn("bonus_mag_attack").AsInt32().WithDefaultValue(0)
            .WithColumn("bonus_defense").AsInt32().WithDefaultValue(0)
            .WithColumn("bonus_resistance").AsInt32().WithDefaultValue(0)
            .WithColumn("bonus_attack_speed").AsDouble().WithDefaultValue(0.0)
            .WithColumn("bonus_crit_chance").AsDouble().WithDefaultValue(0.0)
            .WithColumn("bonus_crit_damage").AsDouble().WithDefaultValue(0.0)
            .WithColumn("bonus_evade_chance").AsDouble().WithDefaultValue(0.0)
            .WithColumn("template_id").AsString().WithDefaultValue("")
            .WithColumn("quantity").AsInt32().WithDefaultValue(1)
            .WithColumn("damage_min").AsInt32().WithDefaultValue(0)
            .WithColumn("damage_max").AsInt32().WithDefaultValue(0)
            .WithColumn("attack_range").AsInt32().WithDefaultValue(1);

        // ── monsters ──
        Create.Table("monsters")
            .WithColumn("id").AsString().NotNullable().PrimaryKey()
            .WithColumn("name").AsString().NotNullable()
            .WithColumn("tier").AsInt32().WithDefaultValue(1)
            .WithColumn("health").AsInt32().WithDefaultValue(10)
            .WithColumn("phys_attack").AsInt32().WithDefaultValue(1)
            .WithColumn("phys_defense").AsInt32().WithDefaultValue(0)
            .WithColumn("xp_reward").AsInt32().WithDefaultValue(1)
            .WithColumn("gold_reward").AsInt32().WithDefaultValue(1)
            .WithColumn("symbol").AsString().WithDefaultValue("M")
            .WithColumn("strength").AsInt32().WithDefaultValue(1)
            .WithColumn("endurance").AsInt32().WithDefaultValue(1)
            .WithColumn("agility").AsInt32().WithDefaultValue(1)
            .WithColumn("cunning").AsInt32().WithDefaultValue(1)
            .WithColumn("intellect").AsInt32().WithDefaultValue(1)
            .WithColumn("wisdom").AsInt32().WithDefaultValue(1)
            .WithColumn("crit_chance").AsDouble().WithDefaultValue(1.0)
            .WithColumn("crit_damage").AsDouble().WithDefaultValue(1.5)
            .WithColumn("evade_chance").AsDouble().WithDefaultValue(1.0);

        // ── quests_def ──
        Create.Table("quests_def")
            .WithColumn("id").AsString().NotNullable().PrimaryKey()
            .WithColumn("title").AsString().NotNullable()
            .WithColumn("description").AsString().WithDefaultValue("")
            .WithColumn("type").AsString().WithDefaultValue("kill")
            .WithColumn("target_monster_id").AsString().WithDefaultValue("")
            .WithColumn("target_item_id").AsString().WithDefaultValue("")
            .WithColumn("target_npc_id").AsString().WithDefaultValue("")
            .WithColumn("target").AsInt32().WithDefaultValue(1)
            .WithColumn("xp_reward").AsInt32().WithDefaultValue(0)
            .WithColumn("gold_reward").AsInt32().WithDefaultValue(0);

        // ── quests ──
        Create.Table("quests")
            .WithColumn("id").AsInt32().PrimaryKey().Identity()
            .WithColumn("player_name").AsString().NotNullable()
            .WithColumn("quest_id").AsString().NotNullable()
            .WithColumn("current").AsInt32().WithDefaultValue(0)
            .WithColumn("completed").AsInt32().WithDefaultValue(0);

        // ── world_config ──
        Create.Table("world_config")
            .WithColumn("key").AsString().NotNullable().PrimaryKey()
            .WithColumn("value").AsInt32().NotNullable();

        // ── npcs ──
        Create.Table("npcs")
            .WithColumn("id").AsString().NotNullable().PrimaryKey()
            .WithColumn("name").AsString().NotNullable()
            .WithColumn("type").AsString().NotNullable()
            .WithColumn("x").AsInt32().WithDefaultValue(0)
            .WithColumn("y").AsInt32().WithDefaultValue(0)
            .WithColumn("data").AsString().Nullable();

        // ── merchant_stock ──
        Execute.Sql(@"
            CREATE TABLE merchant_stock (
                npc_id TEXT NOT NULL,
                item_id TEXT NOT NULL,
                PRIMARY KEY (npc_id, item_id)
            )");

        // ── skills ──
        Create.Table("skills")
            .WithColumn("id").AsString().NotNullable().PrimaryKey()
            .WithColumn("name").AsString().NotNullable()
            .WithColumn("description").AsString().WithDefaultValue("")
            .WithColumn("type").AsString().WithDefaultValue("active")
            .WithColumn("mp_cost").AsInt32().WithDefaultValue(0)
            .WithColumn("cooldown_ms").AsInt32().WithDefaultValue(0)
            .WithColumn("damage_multiplier").AsDouble().WithDefaultValue(1.0)
            .WithColumn("min_level").AsInt32().WithDefaultValue(1)
            .WithColumn("skill_point_cost").AsInt32().WithDefaultValue(1)
            .WithColumn("parent_id").AsString().Nullable()
            .WithColumn("tier").AsInt32().WithDefaultValue(1);

        // ── loot_tables ──
        Create.Table("loot_tables")
            .WithColumn("id").AsInt32().Identity().PrimaryKey()
            .WithColumn("monster_id").AsString().NotNullable()
            .WithColumn("name").AsString().NotNullable()
            .WithColumn("description").AsString().NotNullable().WithDefaultValue("")
            .WithColumn("value").AsInt32().WithDefaultValue(1)
            .WithColumn("drop_chance").AsInt32().WithDefaultValue(30);

        // ── friends ──
        Create.Table("friends")
            .WithColumn("owner_name").AsString().NotNullable()
            .WithColumn("friend_name").AsString().NotNullable()
            .WithColumn("created_at").AsString().NotNullable()
            .WithColumn("note").AsString().WithDefaultValue("");
        Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS uq_friends_pair ON friends (owner_name, friend_name)");
        Create.Index("ix_friends_owner").OnTable("friends").OnColumn("owner_name");

        // ── equipment_slots ──
        Create.Table("equipment_slots")
            .WithColumn("id").AsString().NotNullable().PrimaryKey()
            .WithColumn("name_ru").AsString().NotNullable()
            .WithColumn("is_paperdoll").AsInt32().WithDefaultValue(0)
            .WithColumn("z_order").AsInt32().WithDefaultValue(0)
            .WithColumn("accepts_two_handed").AsInt32().WithDefaultValue(0)
            .WithColumn("blocked_by_two_handed").AsInt32().WithDefaultValue(0);

        // ── player_equipment ──
        Create.Table("player_equipment")
            .WithColumn("player_name").AsString().NotNullable()
            .WithColumn("slot").AsString().NotNullable()
            .WithColumn("item_id").AsString().NotNullable()
            .WithColumn("item_data").AsString().Nullable();

        // ── zones ──
        Create.Table("zones")
            .WithColumn("id").AsString().NotNullable().PrimaryKey()
            .WithColumn("name").AsString().NotNullable()
            .WithColumn("width").AsInt32().WithDefaultValue(100)
            .WithColumn("height").AsInt32().WithDefaultValue(100)
            .WithColumn("spawn_x").AsInt32().WithDefaultValue(50)
            .WithColumn("spawn_y").AsInt32().WithDefaultValue(50)
            .WithColumn("pvp_enabled").AsInt32().WithDefaultValue(0);

        // ── world_portals ──
        Create.Table("world_portals")
            .WithColumn("id").AsString().NotNullable().PrimaryKey()
            .WithColumn("from_zone").AsString().NotNullable()
            .WithColumn("from_x").AsInt32().NotNullable()
            .WithColumn("from_y").AsInt32().NotNullable()
            .WithColumn("to_zone").AsString().NotNullable()
            .WithColumn("to_x").AsInt32().NotNullable()
            .WithColumn("to_y").AsInt32().NotNullable();

        // ══════════════════════════════════════════════════════════
        // SEED DATA
        // ══════════════════════════════════════════════════════════

        SeedItems();
        SeedMonsters();
        SeedLootTables();
        SeedQuests();
        SeedWorldConfig();
        SeedNPCs();
        SeedSkills();
        SeedEquipmentSlots();
        SeedZones();
        SeedPortals();
    }

    private void SeedItems()
    {
        // ── Swords (I0001-I0005): type=weapon, damage_type=slashing, speed=1.0, subtype=sword ──
        InsItem("I0001", "Ржавый меч", "weapon", 1, 0, 0, 0, 0, 1, "Покрытый ржавчиной, но ещё работает", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "slashing", 1.0, "sword", 2, 2, 1);
        InsItem("I0002", "Железный меч", "weapon", 5, 0, 0, 0, 0, 1, "Надёжный железный меч", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "slashing", 1.0, "sword", 5, 5, 1);
        InsItem("I0003", "Стальной меч", "weapon", 20, 0, 0, 0, 0, 1, "Острый стальной клинок", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "slashing", 1.0, "sword", 6, 6, 1);
        InsItem("I0004", "Эбонитовый меч", "weapon", 50, 0, 0, 0, 0, 1, "Тёмный меч из эбонита", 1, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0.1, 0, 0, 0, 0, "slashing", 1.0, "sword", 9, 9, 1);
        InsItem("I0005", "Мифриловый меч", "weapon", 150, 0, 0, 0, 0, 1, "Легендарный меч из мифрила", 3, 0, 2, 0, 0, 0, 0, 0, 0, 0, 2.0, 0.3, 0, 0, 0, "slashing", 1.0, "sword", 15, 15, 1);


        // ── Chest armor (I0006-I0010): type=chest ──
        InsItem("I0006", "Ржавая кираса", "chest", 3, 0, 1, 0, 0, 1, "Потрёпанная, но кое-как защищает", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "", 1.0, "", 0, 0, 1);
        InsItem("I0007", "Железная броня", "chest", 10, 0, 3, 0, 0, 1, "Крепкая железная броня", 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "", 1.0, "", 0, 0, 1);
        InsItem("I0008", "Стальная броня", "chest", 25, 0, 7, 0, 0, 1, "Прочная стальная броня", 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "", 1.0, "", 0, 0, 1);
        InsItem("I0009", "Эбонитовая броня", "chest", 100, 0, 15, 0, 0, 1, "Тёмная броня из эбонита", 0, 4, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 2.0, 0, "", 1.0, "", 0, 0, 1);
        InsItem("I0010", "Мифриловая броня", "chest", 450, 0, 30, 0, 0, 1, "Легендарная броня из мифрила", 0, 8, 0, 0, 0, 3, 0, 0, 0, 0, 0, 0, 0, 5.0, 0, "", 1.0, "", 0, 0, 1);

        // ── Rings (I0011-I0013): type=ring ──
        InsItem("I0011", "Железное кольцо", "ring", 30, 0, 0, 0, 0, 1, "Простое железное кольцо", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "", 1.0, "", 0, 0, 1);
        InsItem("I0012", "Кольцо жизни", "ring", 50, 0, 0, 20, 0, 1, "+20 к максимальному здоровью", 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "", 1.0, "", 0, 0, 1);
        InsItem("I0013", "Изумрудное кольцо", "ring", 100, 0, 5, 5, 0, 1, "+5 атаки, +5 защиты, +5 HP", 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 1.0, 0.1, 1.0, 0, 0, "", 1.0, "", 0, 0, 1);


        // ── Consumables ──
        InsItem("I0014", "Зелье здоровья", "consumable", 20, 0, 0, 0, 50, 1, "Восстанавливает 50 HP", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "", 1.0, "", 0, 0, 1);
        InsItem("I0020", "Зелье маны", "consumable", 25, 0, 0, 0, 0, 1, "Восстанавливает 40 MP", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "", 1.0, "", 0, 0, 1, 40);

        // ── Collectibles (I0015-I0019) ──
        InsItem("I0015", "Ягоды", "collectible", 1, 0, 0, 0, 0, 99, "Сочные ягоды, годятся для квестов", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "", 1.0, "", 0, 0, 1);
        InsItem("I0016", "Грибы", "collectible", 1, 0, 0, 0, 0, 99, "Свежие грибы, годятся для квестов", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "", 1.0, "", 0, 0, 1);
        InsItem("I0017", "Мёд", "collectible", 2, 0, 0, 0, 0, 99, "Сладкий мёд, годится для квестов", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "", 1.0, "", 0, 0, 1);
        InsItem("I0018", "Трава", "collectible", 1, 0, 0, 0, 0, 99, "Простая трава. Может пригодиться алхимику.", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "", 1.0, "", 0, 0, 1);
        InsItem("I0019", "Руда", "collectible", 3, 0, 0, 0, 0, 99, "Самородная руда. Ценный материал.", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "", 1.0, "", 0, 0, 1);

        // ── Helmets (I0201-I0202) ──
        InsItem("I0201", "Железный шлем", "helmet", 15, 0, 2, 5, 0, 1, "Простой защитный шлем", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "", 1.0, "", 0, 0, 1);
        InsItem("I0202", "Стальной шлем", "helmet", 40, 0, 5, 12, 0, 1, "Крепкий шлем из стали", 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "", 1.0, "", 0, 0, 1);

        // ── Cloaks (I0203-I0204) ──
        InsItem("I0203", "Поношенный плащ", "cloak", 10, 0, 1, 0, 0, 1, "Лёгкий плащ, чуть укрывает от ударов", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2.0, 0, "", 1.0, "", 0, 0, 1);
        InsItem("I0204", "Шёлковый плащ", "cloak", 35, 0, 2, 0, 0, 1, "Тонкий плащ, ускользает от врагов", 0, 0, 1, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 5.0, 0, "", 1.0, "", 0, 0, 1);

        // ── Legs (I0205-I0206) ──
        InsItem("I0205", "Железные поножи", "legs", 15, 0, 2, 5, 0, 1, "Защита для ног", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "", 1.0, "", 0, 0, 1);
        InsItem("I0206", "Стальные поножи", "legs", 45, 0, 5, 15, 0, 1, "Тяжёлые поножи", 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "", 1.0, "", 0, 0, 1);

        // ── Boots (I0207-I0208) ──
        InsItem("I0207", "Кожаные сапоги", "boots", 10, 0, 1, 0, 0, 1, "Удобная обувь", 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 3.0, 0, "", 1.0, "", 0, 0, 1);
        InsItem("I0208", "Стальные сапоги", "boots", 35, 0, 3, 0, 0, 1, "Прочные сапоги", 0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 6.0, 0, "", 1.0, "", 0, 0, 1);

        // ── Shields (I0211-I0212) ──
        InsItem("I0211", "Деревянный щит", "shield", 15, 0, 3, 0, 0, 1, "Простой щит", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "", 1.0, "", 0, 0, 1);
        InsItem("I0212", "Стальной щит", "shield", 40, 0, 7, 0, 0, 1, "Надёжный щит", 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "", 1.0, "", 0, 0, 1);

        // ── Necklaces (I0213-I0214) ──
        InsItem("I0213", "Серебряное ожерелье", "necklace", 25, 0, 1, 5, 0, 1, "Украшение с магией", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "", 1.0, "", 0, 0, 1);
        InsItem("I0214", "Ожерелье мудреца", "necklace", 60, 0, 0, 10, 0, 1, "Хранит знание", 0, 0, 0, 0, 2, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, "", 1.0, "", 0, 0, 1);

        // ── Old twohanders (I0215-I0216) ──
        InsItem("I0215", "Двуручный топор", "twohand", 50, 0, 0, 0, 0, 1, "Тяжёлый топор, требует обеих рук", 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, "", 1.0, "", 0, 0, 1);
        InsItem("I0216", "Двуручный меч", "twohand", 70, 0, 0, 0, 0, 1, "Огромный клинок", 3, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, "", 1.0, "", 0, 0, 1);

        // ── Axes (I0301-I0305): slashing, 0.8, axe ──
        InsItem("I0301", "Ржавый топор", "weapon", 2, 0, 0, 0, 0, 1, "Тяжёлый, но рабочий", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0.05, 0, 0, 0, 0, "slashing", 0.8, "axe", 2, 2, 1);
        InsItem("I0302", "Железный топор", "weapon", 8, 0, 0, 0, 0, 1, "Крепкий топор", 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0.05, 0, 0, 0, 0, "slashing", 0.8, "axe", 5, 5, 1);
        InsItem("I0303", "Стальной топор", "weapon", 25, 0, 0, 0, 0, 1, "Острый стальной топор", 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0.1, 0, 0, 0, "slashing", 0.8, "axe", 8, 8, 1);
        InsItem("I0304", "Эбонитовый топор", "weapon", 60, 0, 0, 0, 0, 1, "Тёмный топор из эбонита", 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0.15, 0, 0, 0, "slashing", 0.8, "axe", 11, 11, 1);
        InsItem("I0305", "Мифриловый топор", "weapon", 180, 0, 0, 0, 0, 1, "Легендарный топор", 5, 1, 0, 0, 0, 0, 0, 0, 0, 0, 2, 0.2, 0, 0, 0, "slashing", 0.8, "axe", 18, 18, 1);

        // ── Maces (I0306-I0310): blunt, 0.6, mace ──
        InsItem("I0306", "Ржавая булава", "weapon", 2, 0, 0, 0, 0, 1, "Примитивная, но тяжёлая", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0.05, 0, 0, 0, 0, "blunt", 0.6, "mace", 3, 3, 1);
        InsItem("I0307", "Железная булава", "weapon", 10, 0, 0, 0, 0, 1, "Массивная железная булава", 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0.05, 0, 0, 0, 0, "blunt", 0.6, "mace", 6, 6, 1);
        InsItem("I0308", "Стальная булава", "weapon", 30, 0, 0, 0, 0, 1, "Стальная булава с шипами", 2, 1, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0.1, 0, 0, 0, "blunt", 0.6, "mace", 11, 11, 1);
        InsItem("I0309", "Эбонитовая булава", "weapon", 70, 0, 0, 0, 0, 1, "Тёмная булава из эбонита", 4, 1, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0.15, 0, 0, 0, "blunt", 0.6, "mace", 16, 16, 1);
        InsItem("I0310", "Мифриловая булава", "weapon", 200, 0, 0, 0, 0, 1, "Легендарная булава", 6, 2, 0, 0, 0, 0, 0, 0, 0, 0, 2, 0.2, 0, 0, 0, "blunt", 0.6, "mace", 24, 24, 1);

        // ── Hammers (I0311-I0315): blunt, 0.5, hammer ──
        InsItem("I0311", "Ржавый молот", "weapon", 3, 0, 0, 0, 0, 1, "Тяжёлый и неуклюжий", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0.1, 0, 0, 0, 0, "blunt", 0.5, "hammer", 5, 5, 1);
        InsItem("I0312", "Железный молот", "weapon", 12, 0, 0, 0, 0, 1, "Крепкий боевой молот", 2, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0.1, 0, 0, 0, 0, "blunt", 0.5, "hammer", 8, 8, 1);
        InsItem("I0313", "Стальной молот", "weapon", 35, 0, 0, 0, 0, 1, "Молот из закалённой стали", 3, 1, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0.15, 0, 0, 0, "blunt", 0.5, "hammer", 13, 13, 1);
        InsItem("I0314", "Эбонитовый молот", "weapon", 80, 0, 0, 0, 0, 1, "Тёмный молот из эбонита", 5, 2, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0.2, 0, 0, 0, "blunt", 0.5, "hammer", 19, 19, 1);
        InsItem("I0315", "Мифриловый молот", "weapon", 220, 0, 0, 0, 0, 1, "Легендарный боевой молот", 7, 3, 0, 0, 0, 0, 0, 0, 0, 0, 2, 0.25, 0, 0, 0, "blunt", 0.5, "hammer", 27, 27, 1);

        // ── Daggers (I0316-I0320): piercing, 1.3, dagger ──
        InsItem("I0316", "Ржавый кинжал", "weapon", 1, 0, 0, 0, 0, 1, "Быстрый, но слабый", 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 0, 0, "piercing", 1.3, "dagger", 1, 1, 1);
        InsItem("I0317", "Железный кинжал", "weapon", 4, 0, 0, 0, 0, 1, "Острый железный кинжал", 0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 1, 0, 2, 0, 0, "piercing", 1.3, "dagger", 3, 3, 1);
        InsItem("I0318", "Стальной кинжал", "weapon", 18, 0, 0, 0, 0, 1, "Стальной кинжал убийцы", 0, 0, 3, 0, 0, 0, 0, 0, 0, 0, 2, 0, 3, 0, 0, "piercing", 1.3, "dagger", 6, 6, 1);
        InsItem("I0319", "Эбонитовый кинжал", "weapon", 45, 0, 0, 0, 0, 1, "Тёмный кинжал из эбонита", 0, 0, 4, 1, 0, 0, 0, 0, 0, 0, 2, 0.05, 4, 0, 0, "piercing", 1.3, "dagger", 10, 10, 1);
        InsItem("I0320", "Мифриловый кинжал", "weapon", 140, 0, 0, 0, 0, 1, "Легендарный кинжал", 0, 0, 6, 2, 0, 0, 0, 0, 0, 0, 3, 0.1, 6, 0, 0, "piercing", 1.3, "dagger", 14, 14, 1);


        // ── Maces (I0306-I0310): blunt, 0.6, mace ──






        // ── Hammers (I0311-I0315): blunt, 0.5, hammer ──






        // ── Daggers (I0316-I0320): piercing, 1.3, dagger ──






        // ── 2H Swords (I0401-I0405): slashing, 0.75, greatsword ──
        InsItem("I0401", "Ржавый двуручный меч", "twohand", 3, 0, 0, 0, 0, 1, "Тяжёлый ржавый клинок", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, "slashing", 0.75, "greatsword", 3, 3, 1);
        InsItem("I0402", "Железный двуручный меч", "twohand", 10, 0, 0, 0, 0, 1, "Крепкий железный двуручник", 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, "slashing", 0.75, "greatsword", 7, 7, 1);
        InsItem("I0403", "Стальной двуручный меч", "twohand", 30, 0, 0, 0, 0, 1, "Острый стальной двуручник", 2, 0, 1, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 1, "slashing", 0.75, "greatsword", 10, 10, 1);
        InsItem("I0404", "Эбонитовый двуручный меч", "twohand", 75, 0, 0, 0, 0, 1, "Тёмный двуручник из эбонита", 3, 0, 1, 0, 0, 0, 0, 0, 0, 0, 1, 0.15, 0, 0, 1, "slashing", 0.75, "greatsword", 14, 14, 1);
        InsItem("I0405", "Мифриловый двуручный меч", "twohand", 220, 0, 0, 0, 0, 1, "Легендарный двуручный клинок", 5, 0, 2, 0, 0, 0, 0, 0, 0, 0, 2, 0.3, 0, 0, 1, "slashing", 0.75, "greatsword", 22, 22, 1);

        // ── Greataxes (I0406-I0410): slashing, 0.65, greataxe ──
        InsItem("I0406", "Ржавая секира", "twohand", 4, 0, 0, 0, 0, 1, "Тяжёлый топор на длинной рукояти", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0.08, 0, 1, "slashing", 0.65, "greataxe", 4, 4, 1);
        InsItem("I0407", "Железная секира", "twohand", 12, 0, 0, 0, 0, 1, "Железная секира", 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0.08, 0, 1, "slashing", 0.65, "greataxe", 8, 8, 1);
        InsItem("I0408", "Стальная секира", "twohand", 35, 0, 0, 0, 0, 1, "Острая стальная секира", 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0.12, 0, 0, 1, "slashing", 0.65, "greataxe", 12, 12, 1);
        InsItem("I0409", "Эбонитовая секира", "twohand", 85, 0, 0, 0, 0, 1, "Тёмная секира из эбонита", 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0.18, 0, 0, 1, "slashing", 0.65, "greataxe", 17, 17, 1);
        InsItem("I0410", "Мифриловая секира", "twohand", 250, 0, 0, 0, 0, 1, "Легендарная секира", 5, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 0.25, 0, 0, 1, "slashing", 0.65, "greataxe", 27, 27, 1);

        // ── 2H Hammers (I0411-I0415): blunt, 0.5, greathammer ──
        InsItem("I0411", "Ржавый двуручный молот", "twohand", 5, 0, 0, 0, 0, 1, "Массивный ржавый молот", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0.1, 0, 1, "blunt", 0.5, "greathammer", 8, 8, 1);
        InsItem("I0412", "Железный двуручный молот", "twohand", 15, 0, 0, 0, 0, 1, "Железный боевой молот", 2, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0.1, 0, 1, "blunt", 0.5, "greathammer", 12, 12, 1);
        InsItem("I0413", "Стальной двуручный молот", "twohand", 45, 0, 0, 0, 0, 1, "Стальной двуручный молот", 3, 1, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0.15, 0, 0, 1, "blunt", 0.5, "greathammer", 20, 20, 1);
        InsItem("I0414", "Эбонитовый двуручный молот", "twohand", 100, 0, 0, 0, 0, 1, "Тёмный молот из эбонита", 5, 2, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0.2, 0, 0, 1, "blunt", 0.5, "greathammer", 29, 29, 1);
        InsItem("I0415", "Мифриловый двуручный молот", "twohand", 300, 0, 0, 0, 0, 1, "Легендарный двуручный молот", 7, 3, 0, 0, 0, 0, 0, 0, 0, 0, 2, 0.25, 0, 0, 1, "blunt", 0.5, "greathammer", 40, 40, 1);

        // ── Halberds (I0416-I0420): slashing, 0.7, halberd ──
        InsItem("I0416", "Ржавая алебарда", "twohand", 3, 0, 0, 0, 0, 1, "Длинное ржавое копьё с лезвием", 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, "slashing", 0.7, "halberd", 3, 3, 1);
        InsItem("I0417", "Железная алебарда", "twohand", 9, 0, 0, 0, 0, 1, "Железная алебарда", 1, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, "slashing", 0.7, "halberd", 7, 7, 1);
        InsItem("I0418", "Стальная алебарда", "twohand", 28, 0, 0, 0, 0, 1, "Острая стальная алебарда", 2, 0, 1, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 1, "slashing", 0.7, "halberd", 11, 11, 1);
        InsItem("I0419", "Эбонитовая алебарда", "twohand", 65, 0, 0, 0, 0, 1, "Тёмная алебарда из эбонита", 3, 0, 2, 0, 0, 0, 0, 0, 0, 0, 1, 0.12, 0, 0, 1, "slashing", 0.7, "halberd", 16, 16, 1);
        InsItem("I0420", "Мифриловая алебарда", "twohand", 200, 0, 0, 0, 0, 1, "Легендарная алебарда", 4, 0, 3, 0, 0, 0, 0, 0, 0, 0, 2, 0.22, 0, 0, 1, "slashing", 0.7, "halberd", 25, 25, 1);

        // ── Spears (I0421-I0425): piercing, 0.8, spear, range=1 ──
        InsItem("I0421", "Ржавое копьё", "twohand", 2, 0, 0, 0, 0, 1, "Длинное ржавое копьё", 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0.5, 0, 0, 0, 1, "piercing", 0.8, "spear", 2, 2, 1);
        InsItem("I0422", "Железное копьё", "twohand", 7, 0, 0, 0, 0, 1, "Железное копьё", 0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0.5, 0, 0, 0, 1, "piercing", 0.8, "spear", 5, 5, 1);
        InsItem("I0423", "Стальное копьё", "twohand", 22, 0, 0, 0, 0, 1, "Острое стальное копьё", 0, 0, 3, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 1, "piercing", 0.8, "spear", 8, 8, 1);
        InsItem("I0424", "Эбонитовое копьё", "twohand", 55, 0, 0, 0, 0, 1, "Тёмное копьё из эбонита", 0, 0, 4, 1, 0, 0, 0, 0, 0, 0, 1, 0.05, 0, 0, 1, "piercing", 0.8, "spear", 12, 12, 1);
        InsItem("I0425", "Мифриловое копьё", "twohand", 170, 0, 0, 0, 0, 1, "Легендарное копьё", 0, 0, 6, 2, 0, 0, 0, 0, 0, 0, 2, 0.1, 0, 0, 1, "piercing", 0.8, "spear", 18, 18, 1);

        // ── Bows (I0501-I0505): piercing, 0.7, bow, range=5 ──
        InsItem("I0501", "Ржавый лук", "twohand", 4, 0, 0, 0, 0, 1, "Простой ржавый лук", 0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0.5, 0, 0, 0, 1, "piercing", 0.7, "bow", 2, 2, 5);
        InsItem("I0502", "Железный лук", "twohand", 12, 0, 0, 0, 0, 1, "Крепкий железный лук", 0, 0, 4, 0, 0, 0, 0, 0, 0, 0, 0.5, 0, 0, 0, 1, "piercing", 0.7, "bow", 5, 5, 5);
        InsItem("I0503", "Стальной лук", "twohand", 35, 0, 0, 0, 0, 1, "Острый стальной лук", 0, 0, 6, 0, 0, 0, 0, 0, 0, 0, 1, 0.1, 0, 0, 1, "piercing", 0.7, "bow", 9, 9, 5);
        InsItem("I0504", "Эбонитовый лук", "twohand", 80, 0, 0, 0, 0, 1, "Тёмный лук из эбонита", 0, 0, 8, 0, 0, 0, 0, 0, 0, 0, 1.5, 0.15, 0, 0, 1, "piercing", 0.7, "bow", 14, 14, 5);
        InsItem("I0505", "Мифриловый лук", "twohand", 250, 0, 0, 0, 0, 1, "Легендарный мифриловый лук", 0, 0, 10, 1, 0, 0, 0, 0, 0, 0, 2, 0.25, 0, 0, 1, "piercing", 0.7, "bow", 22, 22, 5);

        // ── Staffs (I0506-I0510): magic, 0.65, staff, range=4 ──
        InsItem("I0506", "Дубинка ученика", "twohand", 3, 0, 0, 0, 0, 1, "Простая деревянная палка", 0, 0, 0, 2, 0, 0, 0, 3, 0, 0, 0, 0, 0, 0, 1, "magic", 0.65, "staff", 1, 1, 4);
        InsItem("I0507", "Посох подмастерья", "twohand", 10, 0, 0, 0, 0, 1, "Посох подмастерья", 0, 0, 0, 4, 0, 0, 0, 7, 0, 0, 0, 0, 0, 0, 1, "magic", 0.65, "staff", 3, 3, 4);
        InsItem("I0508", "Посох мастера", "twohand", 30, 0, 0, 0, 0, 1, "Мощный посох мастера", 0, 0, 0, 6, 0, 0, 0, 12, 0, 0, 0, 0.1, 0, 0, 1, "magic", 0.65, "staff", 6, 6, 4);
        InsItem("I0509", "Эбонитовый посох", "twohand", 75, 0, 0, 0, 0, 1, "Тёмный посох из эбонита", 0, 0, 0, 8, 1, 0, 0, 18, 0, 0, 0, 0.15, 0, 0, 1, "magic", 0.65, "staff", 10, 10, 4);
        InsItem("I0510", "Посох архимага", "twohand", 220, 0, 0, 0, 0, 1, "Легендарный посох архимага", 0, 0, 0, 10, 2, 0, 0, 28, 0, 0, 0, 0.25, 0, 0, 1, "magic", 0.65, "staff", 16, 16, 4);

        // ── Grimoires (I0516-I0520): type=shield, subtype=grimoire, dmgType=magic, range=4, left-hand caster ──
        InsItem("I0516", "Гримуар ученика", "shield", 8, 0, 0, 0, 0, 1, "Простой гримуар для начинающих", 0, 0, 0, 0, 2, 0, 0, 2, 0, 0, 0, 0, 0, 0, 0, "magic", 1.0, "grimoire", 1, 1, 4);
        InsItem("I0517", "Гримуар подмастерья", "shield", 20, 0, 1, 0, 0, 1, "Гримуар с основами магии", 0, 0, 0, 0, 3, 0, 0, 4, 0, 0, 0, 0, 0, 0, 0, "magic", 1.0, "grimoire", 1, 2, 4);
        InsItem("I0518", "Гримуар мага", "shield", 45, 0, 2, 0, 0, 1, "Гримуар с продвинутыми заклинаниями", 0, 0, 0, 0, 4, 0, 0, 6, 0, 0, 0, 0, 0, 0, 0, "magic", 1.0, "grimoire", 3, 3, 4);
        InsItem("I0519", "Эбонитовый гримуар", "shield", 100, 0, 3, 0, 0, 1, "Тёмный гримуар из эбонита", 0, 0, 0, 0, 5, 0, 0, 10, 0, 0, 0, 0, 0, 0, 0, "magic", 1.0, "grimoire", 5, 5, 4);
        InsItem("I0520", "Гримуар архимага", "shield", 280, 0, 6, 0, 0, 1, "Легендарный гримуар архимага", 0, 0, 0, 0, 6, 0, 0, 16, 0, 0, 0, 0, 0, 0, 0, "magic", 1.0, "grimoire", 8, 8, 4);

        // ── Spheres (I0521-I0525): type=shield, subtype=sphere, dmgType=magic, range=4, left-hand caster ──
        InsItem("I0521", "Сфера ученика", "shield", 8, 0, 0, 0, 0, 1, "Магическая сфера для начинающих", 0, 0, 0, 0, 2, 0, 0, 2, 0, 0, 0, 0, 0, 0, 0, "magic", 1.0, "sphere", 1, 1, 4);
        InsItem("I0522", "Сфера подмастерья", "shield", 20, 0, 1, 0, 0, 1, "Сфера с основами магии", 0, 0, 0, 0, 3, 0, 0, 4, 0, 0, 0, 0, 0, 0, 0, "magic", 1.0, "sphere", 1, 2, 4);
        InsItem("I0523", "Сфера мага", "shield", 45, 0, 2, 0, 0, 1, "Сфера с продвинутыми заклинаниями", 0, 0, 0, 0, 4, 0, 0, 6, 0, 0, 0, 0, 0, 0, 0, "magic", 1.0, "sphere", 3, 3, 4);
        InsItem("I0524", "Эбонитовая сфера", "shield", 100, 0, 3, 0, 0, 1, "Тёмная сфера из эбонита", 0, 0, 0, 0, 5, 0, 0, 10, 0, 0, 0, 0, 0, 0, 0, "magic", 1.0, "sphere", 5, 5, 4);
        InsItem("I0525", "Сфера архимага", "shield", 280, 0, 6, 0, 0, 1, "Легендарная сфера архимага", 0, 0, 0, 0, 6, 0, 0, 16, 0, 0, 0, 0, 0, 0, 0, "magic", 1.0, "sphere", 8, 8, 4);

        // ── Gloves (I0601-I0603): glove ──
        InsItem("I0601", "Кожаные перчатки", "glove", 10, 0, 1, 0, 0, 1, "Лёгкие перчатки", 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "", 1.0, "", 0, 0, 1);
        InsItem("I0602", "Стальные перчатки", "glove", 30, 0, 2, 0, 0, 1, "Боевые перчатки", 0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "", 1.0, "", 0, 0, 1);
        InsItem("I0603", "Кольчужные перчатки", "glove", 50, 0, 4, 10, 0, 1, "Прочные кольчужные перчатки", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "", 1.0, "", 0, 0, 1);

        // ── Belts (I0604-I0606): belt ──
        InsItem("I0604", "Кожаный пояс", "belt", 8, 0, 0, 0, 0, 1, "Простой пояс", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "", 1.0, "", 0, 0, 1);
        InsItem("I0605", "Ремень воина", "belt", 25, 0, 1, 5, 0, 1, "Крепкий ремень с пряжкой", 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "", 1.0, "", 0, 0, 1);
        InsItem("I0606", "Пояс силы", "belt", 60, 0, 2, 10, 0, 1, "Магический пояс, усиливает хватку", 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "", 1.0, "", 0, 0, 1);

        // ── Merchant stock (N0001) ──
        string[] merchantItems = {
            "I0001","I0002","I0003","I0004","I0005",
            "I0301","I0302","I0303","I0304","I0305",
            "I0306","I0307","I0308","I0309","I0310",
            "I0311","I0312","I0313","I0314","I0315",
            "I0316","I0317","I0318","I0319","I0320",
            "I0401","I0402","I0403","I0404","I0405",
            "I0406","I0407","I0408","I0409","I0410",
            "I0411","I0412","I0413","I0414","I0415",
            "I0416","I0417","I0418","I0419","I0420",
            "I0421","I0422","I0423","I0424","I0425",
             "I0501","I0502","I0503","I0504","I0505",
             "I0506","I0507","I0508","I0509","I0510",
             "I0511","I0512","I0513","I0514","I0515",
             "I0516","I0517","I0518","I0519","I0520",
             "I0521","I0522","I0523","I0524","I0525",
            "I0211","I0212",
            "I0014","I0020"
        };
        foreach (var itemId in merchantItems)
            Execute.Sql($"INSERT OR IGNORE INTO merchant_stock (npc_id, item_id) VALUES ('N0001','{itemId}')");
    }

    private void InsItem(
        string id, string name, string type, int value,
        int attack, int defense, int maxHpBonus, int healAmount, int stock, string desc,
        int str, int end, int agi, int cun, int intel, int wis,
        int bonusPa, int bonusMa, int bonusDef, int bonusRes,
        double critChance, double critDmg, double evadeChance, double atkSpdBonus,
        int twoHanded,
        string dmgType, double atkSpdMod, string subtype,
        int dmgMin, int dmgMax, int atkRange,
        int restoreMana = 0)
    {
        Execute.Sql(FormattableString.Invariant(
            $"INSERT INTO items ({ItemCols}) VALUES ('{id}','{name}','{type}',{value},{attack},{defense},{maxHpBonus},{healAmount},{restoreMana},{stock},'{Esc(desc)}',{str},{end},{agi},{cun},{intel},{wis},{bonusPa},{bonusMa},{bonusDef},{bonusRes},{atkSpdBonus},{critChance},{critDmg},{evadeChance},{twoHanded},'{dmgType}',{atkSpdMod},'{subtype}',{dmgMin},{dmgMax},{atkRange})"));
    }

    private void SeedMonsters()
    {
        void InsMonster(string id, string name, int tier, int hp, int physAtk, int physDef, int xp, int gold, string sym,
            int str, int end, int agi, int cun, int intel, int wis, double critCh, double critDmg, double evade)
        {
            Execute.Sql(FormattableString.Invariant(
                $"INSERT INTO monsters (id, name, tier, health, phys_attack, phys_defense, xp_reward, gold_reward, symbol, strength, endurance, agility, cunning, intellect, wisdom, crit_chance, crit_damage, evade_chance) VALUES ('{id}','{name}',{tier},{hp},{physAtk},{physDef},{xp},{gold},'{sym}',{str},{end},{agi},{cun},{intel},{wis},{critCh},{critDmg},{evade})"));
        }

        InsMonster("M0001", "Крыса",       1, 15,  3, 1,  5,   2, "r", 1, 1, 2, 1, 1, 1, 1.0, 1.5, 1.0);
        InsMonster("M0002", "Паук",         1, 25,  4, 1,  8,   3, "s", 1, 1, 3, 1, 1, 1, 1.0, 1.5, 1.0);
        InsMonster("M0003", "Зомби",        1, 30,  5, 3, 10,   5, "Z", 2, 2, 1, 1, 1, 1, 1.0, 1.5, 1.0);
        InsMonster("M0004", "Гоблин",       2, 40,  6, 2, 15,   8, "g", 2, 1, 4, 1, 1, 1, 1.0, 1.5, 1.0);
        InsMonster("M0005", "Скелет",       2, 45,  7, 3, 18,  10, "S", 3, 2, 3, 1, 1, 1, 1.0, 1.5, 1.0);
        InsMonster("M0006", "Волк",         2, 55,  9, 3, 22,  12, "w", 3, 2, 5, 1, 1, 1, 1.0, 1.5, 1.0);
        InsMonster("M0007", "Медведь",      3, 80, 12, 5, 40,  25, "B", 5, 3, 3, 1, 1, 1, 1.0, 1.5, 1.0);
        InsMonster("M0008", "Орк",          3, 70, 11, 5, 35,  20, "O", 4, 3, 3, 1, 1, 1, 1.0, 1.5, 1.0);
        InsMonster("M0009", "Тёмный маг",   3, 60, 15, 4, 45,  30, "M", 3, 2, 3, 1, 5, 3, 1.0, 1.5, 1.0);
        InsMonster("M0010", "Дракончик",    4,150, 20, 8, 80,  50, "D", 6, 4, 4, 2, 3, 2, 1.0, 1.5, 1.0);
        InsMonster("M0011", "Дракон",       4,250, 30,12,150, 100, "D", 8, 5, 4, 3, 4, 3, 1.0, 1.5, 1.0);
        InsMonster("M0012", "Лич",          4,200, 25,10,120,  80, "M", 5, 4, 3, 3, 8, 5, 1.0, 1.5, 1.0);
        InsMonster("M0013", "Змея",         1, 20,  4, 1,  6,   3, "n", 1, 1, 3, 2, 1, 1, 1.0, 1.5, 1.0);
    }

    private void SeedLootTables()
    {
        void Ins(string mid, string name, string desc, int value, int chance)
            => Execute.Sql($"INSERT INTO loot_tables (monster_id, name, description, value, drop_chance) VALUES ('{mid}','{Esc(name)}','{Esc(desc)}',{value},{chance})");

        Ins("M0001", "Крысиный хвост",   "Сухой обрубок хвоста. Кто-то коллекционирует такие.", 3, 50);
        Ins("M0001", "Крысиные клыки",    "Маленькие, но острые. Годятся как поделка.", 5, 25);
        Ins("M0002", "Паучья лапа",       "Покрыта мелкими щетинками. Вызывает мурашки.", 6, 40);
        Ins("M0002", "Паучий яд",         "Маленький флакон с ядом. Осторожно!", 10, 20);
        Ins("M0003", "Гнилая плоть",      "Кусок мертвечины. Пахнет ужасно.", 4, 45);
        Ins("M0003", "Костяная булава",   "Ржавая, но ещё может размозжить череп.", 12, 30);
        Ins("M0004", "Гоблинский нож",    "Кривой нож, вырезанный из железа.", 8, 40);
        Ins("M0004", "Гоблинское ухо",    "Трофей охотника. Не для слабонервных.", 6, 25);
        Ins("M0005", "Кость скелета",     "Прочная кость. Пригодится алхимику.", 7, 45);
        Ins("M0005", "Череп скелета",     "Пустые глазницы смотрят в душу.", 10, 30);
        Ins("M0006", "Волчий клык",       "Острый и крепкий. Из него делают подвески.", 15, 35);
        Ins("M0006", "Волчья шкура",      "Густая и тёплая. Ценный мех.", 18, 15);
        Ins("M0007", "Медвежий коготь",   "Массивный коготь. Украшение для воина.", 25, 30);
        Ins("M0007", "Медвежья шкура",    "Толстая шкура, выдержит удар меча.", 30, 12);
        Ins("M0008", "Орочий клык",       "Жёлтый и потрескавшийся. Не самый приятный трофей.", 20, 35);
        Ins("M0008", "Орочий браслет",    "Грубый железный браслет. Красиво смотрится.", 25, 15);
        Ins("M0009", "Тёмный артефакт",   "Мерцающий камень. Шепчет что-то непонятное.", 40, 25);
        Ins("M0009", "Магическая пыль",   "Светящиеся частицы. Используются в ритуалах.", 35, 18);
        Ins("M0010", "Чешуя дракончика",  "Маленькая, но прочная. Блестит на свету.", 50, 20);
        Ins("M0010", "Коготь дракончика", "Острый как бритва. Трогать в перчатках.", 55, 10);
        Ins("M0011", "Драконья чешуя",    "Большая и невероятно прочная. Легендарный материал.", 100, 15);
        Ins("M0011", "Драконье сердце",   "Ещё тёплое. Источник древней силы.", 150, 5);
        Ins("M0012", "Артефакт Лича",     "Посох с мёртвой душой на конце.", 120, 10);
        Ins("M0012", "Кристалл души",     "Заключённая в камень душа. Мерцает холодным светом.", 130, 8);
        Ins("M0013", "Змеиная кожа",      "Гладкая чешуя. Используется в кожевенном деле.", 4, 45);
        Ins("M0013", "Змеиный яд",        "Капля этого яда — смертельна для мышей.", 8, 20);
    }

    private void SeedQuests()
    {
        void InsQuest(string id, string title, string desc, string type, string mid, string iid, string nid, int target, int xp, int gold)
            => Execute.Sql($"INSERT OR IGNORE INTO quests_def (id, title, description, type, target_monster_id, target_item_id, target_npc_id, target, xp_reward, gold_reward) " +
                $"VALUES ('{id}','{Esc(title)}','{Esc(desc)}','{type}','{mid}','{iid}','{nid}',{target},{xp},{gold})");

        InsQuest("Q0001", "Истребление крыс",    "Убейте 5 крыс рядом с торговцем.",       "kill",    "M0001", "",  "N0001", 5,  30,  20);
        InsQuest("Q0002", "Волчья стая",          "Убейте 3 волка.",                          "kill",    "M0006", "",  "",     3,  60,  50);
        InsQuest("Q0003", "Драконоборец",         "Убейте 2 дракона.",                        "kill",    "M0011", "",  "",     2, 200, 150);
        InsQuest("Q0004", "Змеиная угроза",       "Поговори со старостой и убей 3 змеи.",     "kill",    "M0013", "",  "N0003", 3,  40,  25);
        InsQuest("Q0005", "Грибная охота",        "Соберите 6 грибов.",                       "collect", "",     "I0016", "", 6,  30,  20);
        InsQuest("Q0006", "Медовый сезон",         "Соберите 4 мёда из ульев.",               "collect", "",     "I0017", "", 4,  50,  35);
        InsQuest("Q0007", "Волчья стая",          "Перед старостой стая волков угрожает деревне. Убей 5 волков.", "kill", "M0006", "", "N0003", 5, 80, 50);
        InsQuest("Q0008", "Сбор ягод",            "Соберите 8 ягод с кустов.",               "collect", "",     "I0015", "", 8,  25,  15);
    }

    private void SeedWorldConfig()
    {
        Execute.Sql("INSERT INTO world_config (key, value) VALUES ('width', 100)");
        Execute.Sql("INSERT INTO world_config (key, value) VALUES ('height', 100)");
        Execute.Sql("INSERT INTO world_config (key, value) VALUES ('merchant_x', 50)");
        Execute.Sql("INSERT INTO world_config (key, value) VALUES ('merchant_y', 50)");
        Execute.Sql("INSERT INTO world_config (key, value) VALUES ('board_x', 48)");
        Execute.Sql("INSERT INTO world_config (key, value) VALUES ('board_y', 48)");
    }

    private void SeedNPCs()
    {
        // Merchant dialogue
        string merchantDialogue = @"{
  ""greeting"": {
    ""speaker"": ""Торговец"",
    ""text"": ""Добро пожаловать в мой магазин! Чем могу помочь?"",
    ""choices"": [
      { ""text"": ""Покажи товары."", ""next"": null, ""action"": ""open_shop"" },
      { ""text"": ""Есть работа?"", ""next"": ""quest_offer"", ""condition"": ""quest_not_active:Q0001"" },
      { ""text"": ""Я выполнил задание."", ""next"": ""quest_turnin"", ""condition"": ""quest_ready:Q0001"" },
      { ""text"": ""Я ещё собираю хвосты."", ""next"": null, ""condition"": ""quest_active:Q0001"", ""action"": ""close"" },
      { ""text"": ""До свидания."", ""next"": null, ""action"": ""close"" }
    ]
  },
  ""quest_offer"": {
    ""speaker"": ""Торговец"",
    ""text"": ""Крысы завелись в моём подвале. Достань мне 5 крысиных хвостов — хорошо заплачу."",
    ""choices"": [
      { ""text"": ""Беру задание!"", ""next"": ""quest_accept"", ""action"": ""accept_quest:Q0001"" },
      { ""text"": ""Не сейчас."", ""next"": ""greeting"" }
    ]
  },
  ""quest_accept"": {
    ""speaker"": ""Торговец"",
    ""text"": ""Отлично! Крысы водятся рядом со мной. Принеси мне 5 хвостов."",
    ""choices"": [
      { ""text"": ""До встречи."", ""next"": null, ""action"": ""close"" }
    ]
  },
  ""quest_turnin"": {
    ""speaker"": ""Торговец"",
    ""text"": ""Отлично, вот твоя награда! Ты настоящий герой."",
    ""choices"": [
      { ""text"": ""Спасибо!"", ""next"": null, ""action"": ""complete_quest:Q0001"" }
    ]
  }
}";

        // Elder dialogue
        string elderDialogue = @"{
  ""greeting"": {
    ""speaker"": ""Староста деревни"",
    ""text"": ""Приветствую, путник. Наше деревне нужна помощь."",
    ""choices"": [
      { ""text"": ""Что случилось?"", ""next"": ""story1"", ""condition"": ""quest_not_active:Q0007"" },
      { ""text"": ""Как идёт охота на волков?"", ""next"": ""quest_progress"", ""condition"": ""quest_active:Q0007"" },
      { ""text"": ""Волки побеждены. Все пятеро мертвы."", ""next"": ""quest_turnin"", ""condition"": ""quest_ready:Q0007"" },
      { ""text"": ""У меня есть задание от торговца."", ""next"": ""merchant_quest"", ""condition"": ""quest_ready:Q0001"" },
      { ""text"": ""Простите, мне пора."", ""next"": null, ""action"": ""close"" }
    ]
  },
  ""quest_progress"": {
    ""speaker"": ""Староста деревни"",
    ""text"": ""Ты ещё не вернулся с трофеями. Стая всё ещё бродит у околицы. Будь осторожен и возвращайся, когда справишься со всеми пятью."",
    ""choices"": [
      { ""text"": ""Постараюсь быстрее."", ""next"": null, ""action"": ""close"" }
    ]
  },
  ""story1"": {
    ""speaker"": ""Староста деревни"",
    ""text"": ""На прошлой неделе охотник Тихон вышел в лес и не вернуся. Мы нашли его лук сломанным у развилки тропинок. А сегодня утром стая волков вышла к самой околице."",
    ""choices"": [
      { ""text"": ""Волки? Разве это серьёзная угроза?"", ""next"": ""story2"" },
      { ""text"": ""Мне жаль охотника, но у меня свои дела."", ""next"": null, ""action"": ""close"" }
    ]
  },
  ""story2"": {
    ""speaker"": ""Староста деревни"",
    ""text"": ""Серьёзная. Пять зверей — и не простых, а голодных. Они уже покалечили козу у ближайшего двора. Если стая наберётся смелости — нападут на людей. Дети боятся выходить во двор играть."",
    ""choices"": [
      { ""text"": ""Я помогу. Отправлюсь на охоту на волков."", ""next"": ""story_accept"", ""action"": ""accept_quest:Q0007"", ""condition"": ""quest_not_active:Q0007"" },
      { ""text"": ""Я уже охотюсь на них."", ""next"": null, ""condition"": ""quest_active:Q0007"", ""action"": ""close"" },
      { ""text"": ""Пять волков — это серьёзно. Мне нужно подготовиться."", ""next"": null, ""action"": ""close"" }
    ]
  },
  ""story_accept"": {
    ""speaker"": ""Староста деревни"",
    ""text"": ""Благодарю! Будь осторожен — волки хитрые звери. Они держатся вместе и нападают стаей. Убей всех пятерых и возвращайся — я награжу тебя по заслугам."",
    ""choices"": [
      { ""text"": ""Вернусь с победой."", ""next"": null, ""action"": ""close"" }
    ]
  },
  ""quest_turnin"": {
    ""speaker"": ""Староста деревни"",
    ""text"": ""Неужели правда? Все пятеро?! Ты настоящий воин! Деревня будет помнить твой подвиг. Прими мою благодарность — и вот, возьми это. Ты заслужил."",
    ""choices"": [
      { ""text"": ""Спасибо, староста!"", ""next"": null, ""action"": ""complete_quest:Q0007"" }
    ]
  },
  ""merchant_quest"": {
    ""speaker"": ""Староста деревни"",
    ""text"": ""А, ты помогаешь торговцу? Передай ему мой привет. Ты заслужил награду!"",
    ""choices"": [
      { ""text"": ""Спасибо!"", ""next"": null, ""action"": ""complete_quest:Q0001"" }
    ]
  }
}";

        Insert.IntoTable("npcs").Row(new { id = "N0001", name = "Торговец", type = "merchant", x = 50, y = 50, data = merchantDialogue });
        Insert.IntoTable("npcs").Row(new { id = "N0002", name = "Доска заданий", type = "board", x = 48, y = 48, data = (string?)null });
        Insert.IntoTable("npcs").Row(new { id = "N0003", name = "Староста", type = "npc", x = 48, y = 52, data = elderDialogue });
    }

    private void SeedSkills()
    {
        Insert.IntoTable("skills").Row(new { id = "SK0001", name = "Крепкая рука", description = "Увеличивает урон ближней атаки на 15%. 100% шанс прока оружия.", type = "Активные", mp_cost = 0, cooldown_ms = 5000, damage_multiplier = 1.15, min_level = 1, skill_point_cost = 1, parent_id = (string?)null, tier = 1 });
        Insert.IntoTable("skills").Row(new { id = "SK0002", name = "Поток ударов", description = "Накладывает бафф Проворность (+30% к скорости атаки) на 10 секунд.", type = "Активные", mp_cost = 10, cooldown_ms = 20000, damage_multiplier = 1.0, min_level = 1, skill_point_cost = 1, parent_id = "SK0001", tier = 2 });
    }

    private void SeedEquipmentSlots()
    {
        Insert.IntoTable("equipment_slots").Row(new { id = "legs",  name_ru = "Ноги",            is_paperdoll = 1, z_order = 1, accepts_two_handed = 0, blocked_by_two_handed = 0 });
        Insert.IntoTable("equipment_slots").Row(new { id = "feet",  name_ru = "Обувь",           is_paperdoll = 1, z_order = 2, accepts_two_handed = 0, blocked_by_two_handed = 0 });
        Insert.IntoTable("equipment_slots").Row(new { id = "torso", name_ru = "Торс",            is_paperdoll = 1, z_order = 3, accepts_two_handed = 0, blocked_by_two_handed = 0 });
        Insert.IntoTable("equipment_slots").Row(new { id = "head",  name_ru = "Голова",          is_paperdoll = 1, z_order = 4, accepts_two_handed = 0, blocked_by_two_handed = 0 });
        Insert.IntoTable("equipment_slots").Row(new { id = "gloves", name_ru = "Перчатки",       is_paperdoll = 1, z_order = 5, accepts_two_handed = 0, blocked_by_two_handed = 0 });
        Insert.IntoTable("equipment_slots").Row(new { id = "lhand", name_ru = "Левая рука",      is_paperdoll = 1, z_order = 6, accepts_two_handed = 0, blocked_by_two_handed = 1 });
        Insert.IntoTable("equipment_slots").Row(new { id = "belt",  name_ru = "Пояс",            is_paperdoll = 1, z_order = 7, accepts_two_handed = 0, blocked_by_two_handed = 0 });
        Insert.IntoTable("equipment_slots").Row(new { id = "rhand", name_ru = "Правая рука",     is_paperdoll = 1, z_order = 8, accepts_two_handed = 1, blocked_by_two_handed = 0 });
        Insert.IntoTable("equipment_slots").Row(new { id = "neck",  name_ru = "Ожерелье",        is_paperdoll = 0, z_order = 0, accepts_two_handed = 0, blocked_by_two_handed = 0 });
        Insert.IntoTable("equipment_slots").Row(new { id = "ring_r", name_ru = "Кольцо (правая рука)", is_paperdoll = 0, z_order = 0, accepts_two_handed = 0, blocked_by_two_handed = 0 });
        Insert.IntoTable("equipment_slots").Row(new { id = "ring_l", name_ru = "Кольцо (левая рука)",  is_paperdoll = 0, z_order = 0, accepts_two_handed = 0, blocked_by_two_handed = 0 });
        Insert.IntoTable("equipment_slots").Row(new { id = "cloak", name_ru = "Плащ",            is_paperdoll = 0, z_order = 0, accepts_two_handed = 0, blocked_by_two_handed = 0 });
    }

    private void SeedZones()
    {
        Insert.IntoTable("zones").Row(new { id = "main", name = "Основной мир", width = 100, height = 100, spawn_x = 50, spawn_y = 50, pvp_enabled = 0 });
        Insert.IntoTable("zones").Row(new { id = "arena", name = "Арена", width = 30, height = 30, spawn_x = 15, spawn_y = 15, pvp_enabled = 1 });
    }

    private void SeedPortals()
    {
        Insert.IntoTable("world_portals").Row(new { id = "p_main_arena", from_zone = "main", from_x = 50, from_y = 44, to_zone = "arena", to_x = 15, to_y = 28 });
        Insert.IntoTable("world_portals").Row(new { id = "p_arena_main", from_zone = "arena", from_x = 15, from_y = 28, to_zone = "main", to_x = 50, to_y = 45 });
    }

    private static string Esc(string s) => s.Replace("'", "''");
}

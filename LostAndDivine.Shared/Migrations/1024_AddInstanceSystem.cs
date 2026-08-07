using FluentMigrator;

namespace LostAndDivine.Shared.Migrations;

[Migration(1024)]
public class AddInstanceSystem : ForwardOnlyMigration
{
    public override void Up()
    {
        Create.Table("instance_templates")
            .WithColumn("id").AsString(64).PrimaryKey()
            .WithColumn("name").AsString(128).NotNullable()
            .WithColumn("zone_id").AsString(64).NotNullable()
            .WithColumn("time_limit_seconds").AsInt32().NotNullable().WithDefaultValue(600)
            .WithColumn("spawn_x").AsInt32().NotNullable()
            .WithColumn("spawn_y").AsInt32().NotNullable()
            .WithColumn("boss_monster_id").AsString(64).NotNullable()
            .WithColumn("chest_x").AsInt32().NotNullable()
            .WithColumn("chest_y").AsInt32().NotNullable()
            .WithColumn("exit_x").AsInt32().NotNullable()
            .WithColumn("exit_y").AsInt32().NotNullable()
            .WithColumn("corridor_length").AsInt32().NotNullable().WithDefaultValue(20)
            .WithColumn("corridor_width").AsInt32().NotNullable().WithDefaultValue(5);

        Create.Table("instance_portals")
            .WithColumn("id").AsString(64).PrimaryKey()
            .WithColumn("from_zone").AsString(64).NotNullable()
            .WithColumn("from_x").AsInt32().NotNullable()
            .WithColumn("from_y").AsInt32().NotNullable()
            .WithColumn("instance_template_id").AsString(64).NotNullable();

        Create.Table("instance_spawns")
            .WithColumn("id").AsString(64).PrimaryKey()
            .WithColumn("instance_template_id").AsString(64).NotNullable()
            .WithColumn("x").AsInt32().NotNullable()
            .WithColumn("y").AsInt32().NotNullable()
            .WithColumn("monster_template_id").AsString(64).NotNullable()
            .WithColumn("is_boss").AsBoolean().NotNullable().WithDefaultValue(false);

        // Босс подземелья (M0020)
        Execute.Sql(@"INSERT OR IGNORE INTO monsters (id, name, tier, health, phys_attack, phys_defense, xp_reward, gold_reward, symbol, strength, endurance, agility, cunning, intellect, wisdom, crit_chance, crit_damage, evade_chance, block_chance, parry_chance, shield_defense)
            VALUES ('M0020','Комендант подземелья',4,300,25,12,200,150,'K',7,5,4,3,4,3,3.0,2.0,5.0,5.0,5.0,5)");

        // Шаблон инстанса
        Insert.IntoTable("instance_templates").Row(new
        {
            id = "trial_dungeon",
            name = "Подземелье испытаний",
            zone_id = "main",
            time_limit_seconds = 600,
            spawn_x = 1,
            spawn_y = 1,
            boss_monster_id = "M0020",
            chest_x = 2,
            chest_y = 18,
            exit_x = 2,
            exit_y = 19,
            corridor_length = 20,
            corridor_width = 5
        });

        // Портал на главной карте
        Insert.IntoTable("instance_portals").Row(new
        {
            id = "portal_trial",
            from_zone = "main",
            from_x = 48,
            from_y = 55,
            instance_template_id = "trial_dungeon"
        });

        // Спавны мобов (5 обычных + босс)
        Insert.IntoTable("instance_spawns").Row(new { id = "spawn_01", instance_template_id = "trial_dungeon", x = 2, y = 3, monster_template_id = "M0001", is_boss = false });
        Insert.IntoTable("instance_spawns").Row(new { id = "spawn_02", instance_template_id = "trial_dungeon", x = 2, y = 6, monster_template_id = "M0002", is_boss = false });
        Insert.IntoTable("instance_spawns").Row(new { id = "spawn_03", instance_template_id = "trial_dungeon", x = 2, y = 9, monster_template_id = "M0001", is_boss = false });
        Insert.IntoTable("instance_spawns").Row(new { id = "spawn_04", instance_template_id = "trial_dungeon", x = 2, y = 12, monster_template_id = "M0003", is_boss = false });
        Insert.IntoTable("instance_spawns").Row(new { id = "spawn_05", instance_template_id = "trial_dungeon", x = 2, y = 15, monster_template_id = "M0002", is_boss = false });
        Insert.IntoTable("instance_spawns").Row(new { id = "spawn_06", instance_template_id = "trial_dungeon", x = 2, y = 17, monster_template_id = "M0020", is_boss = true });

        // NPC-стражник на клетке портала
        Insert.IntoTable("npcs").Row(new { id = "N0010", name = "Страж подземелья", type = "instance_portal", x = 48, y = 55, data = "{}" });
    }
}

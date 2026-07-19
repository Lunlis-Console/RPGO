using FluentMigrator;

namespace RPGGame.Shared.Migrations;

[Migration(1)]
public class InitialSchema : ForwardOnlyMigration
{
    public override void Up()
    {
        Create.Table("accounts")
            .WithColumn("login").AsString().NotNullable().PrimaryKey()
            .WithColumn("password_hash").AsString().NotNullable()
            .WithColumn("player_name").AsString().NotNullable().Unique()
            .WithColumn("level").AsInt32().WithDefaultValue(1)
            .WithColumn("experience").AsInt32().WithDefaultValue(0)
            .WithColumn("health").AsInt32().WithDefaultValue(100)
            .WithColumn("max_health").AsInt32().WithDefaultValue(100)
            .WithColumn("attack").AsInt32().WithDefaultValue(10)
            .WithColumn("defense").AsInt32().WithDefaultValue(5)
            .WithColumn("gold").AsInt32().WithDefaultValue(0)
            .WithColumn("created_at").AsString().NotNullable()
            .WithColumn("last_login").AsString().NotNullable()
            .WithColumn("weapon_id").AsString().WithDefaultValue("")
            .WithColumn("armor_id").AsString().WithDefaultValue("")
            .WithColumn("accessory_id").AsString().WithDefaultValue("")
            .WithColumn("strength").AsInt32().WithDefaultValue(1)
            .WithColumn("stamina").AsInt32().WithDefaultValue(1)
            .WithColumn("agility").AsInt32().WithDefaultValue(1)
            .WithColumn("cunning").AsInt32().WithDefaultValue(1)
            .WithColumn("wisdom").AsInt32().WithDefaultValue(1)
            .WithColumn("will_val").AsInt32().WithDefaultValue(1)
            .WithColumn("attribute_points").AsInt32().WithDefaultValue(0)
            .WithColumn("speed").AsInt32().WithDefaultValue(1)
            .WithColumn("pos_x").AsInt32().WithDefaultValue(-1)
            .WithColumn("pos_y").AsInt32().WithDefaultValue(-1)
            .WithColumn("hotbar_slots").AsString().WithDefaultValue("");

        Create.Table("inventory")
            .WithColumn("id").AsInt32().PrimaryKey().Identity()
            .WithColumn("player_name").AsString().NotNullable()
            .WithColumn("item_id").AsString().NotNullable()
            .WithColumn("name").AsString().NotNullable()
            .WithColumn("type").AsString().NotNullable()
            .WithColumn("value").AsInt32().WithDefaultValue(0)
            .WithColumn("attack").AsInt32().WithDefaultValue(0)
            .WithColumn("defense").AsInt32().WithDefaultValue(0)
            .WithColumn("max_health_bonus").AsInt32().WithDefaultValue(0)
            .WithColumn("heal_amount").AsInt32().WithDefaultValue(0)
            .WithColumn("description").AsString().WithDefaultValue("")
            .WithColumn("bonus_strength").AsInt32().WithDefaultValue(0)
            .WithColumn("bonus_stamina").AsInt32().WithDefaultValue(0)
            .WithColumn("bonus_agility").AsInt32().WithDefaultValue(0)
            .WithColumn("bonus_cunning").AsInt32().WithDefaultValue(0)
            .WithColumn("bonus_wisdom").AsInt32().WithDefaultValue(0)
            .WithColumn("bonus_will").AsInt32().WithDefaultValue(0)
            .WithColumn("bonus_crit_chance").AsDouble().WithDefaultValue(0.0)
            .WithColumn("bonus_crit_damage").AsDouble().WithDefaultValue(0.0)
            .WithColumn("bonus_evade_chance").AsDouble().WithDefaultValue(0.0);

        Create.Table("items")
            .WithColumn("id").AsString().NotNullable().PrimaryKey()
            .WithColumn("name").AsString().NotNullable()
            .WithColumn("type").AsString().NotNullable()
            .WithColumn("value").AsInt32().WithDefaultValue(0)
            .WithColumn("attack").AsInt32().WithDefaultValue(0)
            .WithColumn("defense").AsInt32().WithDefaultValue(0)
            .WithColumn("max_health_bonus").AsInt32().WithDefaultValue(0)
            .WithColumn("heal_amount").AsInt32().WithDefaultValue(0)
            .WithColumn("stock").AsInt32().WithDefaultValue(1)
            .WithColumn("description").AsString().WithDefaultValue("")
            .WithColumn("bonus_strength").AsInt32().WithDefaultValue(0)
            .WithColumn("bonus_stamina").AsInt32().WithDefaultValue(0)
            .WithColumn("bonus_agility").AsInt32().WithDefaultValue(0)
            .WithColumn("bonus_cunning").AsInt32().WithDefaultValue(0)
            .WithColumn("bonus_wisdom").AsInt32().WithDefaultValue(0)
            .WithColumn("bonus_will").AsInt32().WithDefaultValue(0)
            .WithColumn("bonus_crit_chance").AsDouble().WithDefaultValue(0.0)
            .WithColumn("bonus_crit_damage").AsDouble().WithDefaultValue(0.0)
            .WithColumn("bonus_evade_chance").AsDouble().WithDefaultValue(0.0);

        Create.Table("monsters")
            .WithColumn("id").AsString().NotNullable().PrimaryKey()
            .WithColumn("name").AsString().NotNullable()
            .WithColumn("tier").AsInt32().WithDefaultValue(1)
            .WithColumn("health").AsInt32().WithDefaultValue(10)
            .WithColumn("attack").AsInt32().WithDefaultValue(1)
            .WithColumn("defense").AsInt32().WithDefaultValue(0)
            .WithColumn("xp_reward").AsInt32().WithDefaultValue(1)
            .WithColumn("gold_reward").AsInt32().WithDefaultValue(1)
            .WithColumn("symbol").AsString().WithDefaultValue("M")
            .WithColumn("strength").AsInt32().WithDefaultValue(1)
            .WithColumn("stamina").AsInt32().WithDefaultValue(1)
            .WithColumn("agility").AsInt32().WithDefaultValue(1)
            .WithColumn("cunning").AsInt32().WithDefaultValue(1)
            .WithColumn("wisdom").AsInt32().WithDefaultValue(1)
            .WithColumn("will").AsInt32().WithDefaultValue(1)
            .WithColumn("crit_chance").AsDouble().WithDefaultValue(1.0)
            .WithColumn("crit_damage").AsDouble().WithDefaultValue(1.5)
            .WithColumn("evade_chance").AsDouble().WithDefaultValue(1.0);

        Create.Table("quests_def")
            .WithColumn("id").AsString().NotNullable().PrimaryKey()
            .WithColumn("title").AsString().NotNullable()
            .WithColumn("description").AsString().WithDefaultValue("")
            .WithColumn("type").AsString().WithDefaultValue("kill")
            .WithColumn("target_monster_id").AsString().WithDefaultValue("")
            .WithColumn("target_item_id").AsString().WithDefaultValue("")
            .WithColumn("target").AsInt32().WithDefaultValue(1)
            .WithColumn("xp_reward").AsInt32().WithDefaultValue(0)
            .WithColumn("gold_reward").AsInt32().WithDefaultValue(0);

        Create.Table("quests")
            .WithColumn("id").AsInt32().PrimaryKey().Identity()
            .WithColumn("player_name").AsString().NotNullable()
            .WithColumn("quest_id").AsString().NotNullable()
            .WithColumn("current").AsInt32().WithDefaultValue(0)
            .WithColumn("completed").AsInt32().WithDefaultValue(0);

        Create.Table("world_config")
            .WithColumn("key").AsString().NotNullable().PrimaryKey()
            .WithColumn("value").AsInt32().NotNullable();

        Create.Table("npcs")
            .WithColumn("id").AsString().NotNullable().PrimaryKey()
            .WithColumn("name").AsString().NotNullable()
            .WithColumn("type").AsString().NotNullable()
            .WithColumn("x").AsInt32().WithDefaultValue(0)
            .WithColumn("y").AsInt32().WithDefaultValue(0)
            .WithColumn("data").AsString().Nullable();

        Execute.Sql(@"
            CREATE TABLE merchant_stock (
                npc_id TEXT NOT NULL,
                item_id TEXT NOT NULL,
                PRIMARY KEY (npc_id, item_id)
            )");

        Create.Table("skills")
            .WithColumn("id").AsString().NotNullable().PrimaryKey()
            .WithColumn("name").AsString().NotNullable()
            .WithColumn("description").AsString().WithDefaultValue("")
            .WithColumn("type").AsString().WithDefaultValue("active")
            .WithColumn("mp_cost").AsInt32().WithDefaultValue(0)
            .WithColumn("cooldown_ms").AsInt32().WithDefaultValue(0)
            .WithColumn("damage_multiplier").AsDouble().WithDefaultValue(1.0)
            .WithColumn("min_level").AsInt32().WithDefaultValue(1);
    }
}

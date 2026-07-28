using FluentMigrator;

namespace RPGGame.Shared.Migrations;

[Migration(1014)]
public class AddBlockParry : ForwardOnlyMigration
{
    public override void Up()
    {
        Create.Column("bonus_block_chance").OnTable("items").AsDouble().WithDefaultValue(0);
        Create.Column("bonus_parry_chance").OnTable("items").AsDouble().WithDefaultValue(0);

        Update.Table("items")
            .Set(new { bonus_block_chance = 5.0 })
            .Where(new { id = "I0211" });

        Update.Table("items")
            .Set(new { bonus_block_chance = 8.0 })
            .Where(new { id = "I0212" });

        Create.Column("bonus_block_chance").OnTable("inventory").AsDouble().WithDefaultValue(0);
        Create.Column("bonus_parry_chance").OnTable("inventory").AsDouble().WithDefaultValue(0);

        Create.Column("block_chance").OnTable("monsters").AsDouble().WithDefaultValue(0);
        Create.Column("parry_chance").OnTable("monsters").AsDouble().WithDefaultValue(0);
        Create.Column("shield_defense").OnTable("monsters").AsInt32().WithDefaultValue(0);
    }
}

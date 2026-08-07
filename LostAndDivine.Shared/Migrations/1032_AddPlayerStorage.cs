using FluentMigrator;

namespace LostAndDivine.Shared.Migrations;

/// <summary>
/// Персистентное хранилище (склад) игрока: таблица storage_items.
/// </summary>
[Migration(1032)]
public class AddPlayerStorage : ForwardOnlyMigration
{
    public override void Up()
    {
        Create.Table("storage_items")
            .WithColumn("player_name").AsString(64).NotNullable()
            .WithColumn("item_id").AsString(200).NotNullable()
            .WithColumn("template_id").AsString(200).NotNullable().WithDefaultValue("")
            .WithColumn("name").AsString(200).NotNullable()
            .WithColumn("type").AsString(64).NotNullable()
            .WithColumn("value").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("quantity").AsInt32().NotNullable().WithDefaultValue(1)
            .WithColumn("max_stack").AsInt32().NotNullable().WithDefaultValue(1)
            .WithColumn("description").AsString(1000).NotNullable().WithDefaultValue("")
            .WithColumn("bonus_defense").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("bonus_phys_attack").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("bonus_mag_attack").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("bonus_max_health").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("heal_amount").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("restore_mana").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("weapon_subtype").AsString(64).NotNullable().WithDefaultValue("")
            .WithColumn("damage_min").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("damage_max").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("attack_range").AsInt32().NotNullable().WithDefaultValue(1);

        Create.Index("IX_storage_items_player_name")
            .OnTable("storage_items")
            .OnColumn("player_name");
    }
}

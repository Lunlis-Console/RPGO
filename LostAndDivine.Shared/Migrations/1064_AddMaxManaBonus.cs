using FluentMigrator;

namespace LostAndDivine.Shared.Migrations;

/// <summary>
/// Бонус экипировки к максимальной мане («Бонус к MP»).
/// В items/inventory колонка называется max_mana_bonus (по конвенции max_health_bonus),
/// в storage_items — bonus_max_mana (по конвенции bonus_max_health).
/// </summary>
[Migration(1064)]
public class AddMaxManaBonus : ForwardOnlyMigration
{
    public override void Up()
    {
        foreach (var table in new[] { "items", "inventory" })
            Create.Column("max_mana_bonus").OnTable(table).AsInt32().WithDefaultValue(0);

        Create.Column("bonus_max_mana").OnTable("storage_items").AsInt32().WithDefaultValue(0);
    }
}

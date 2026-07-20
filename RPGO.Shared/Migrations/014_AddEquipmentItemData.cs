using FluentMigrator;

namespace RPGGame.Shared.Migrations;

/// <summary>
/// Храним полный предмет экипировки прямо в player_equipment (item_data, JSON),
/// чтобы не зависеть от таблицы inventory и не дублировать надетые вещи в инвентаре.
/// </summary>
[Migration(14)]
public class AddEquipmentItemData : ForwardOnlyMigration
{
    public override void Up()
    {
        Execute.Sql("ALTER TABLE player_equipment ADD COLUMN item_data TEXT;");
    }
}

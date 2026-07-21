using FluentMigrator;

namespace RPGGame.Shared.Migrations;

/// <summary>
/// Добавляем тип урона и модификатор скорости атаки к предметам оружия.
/// damage_type: slashing / piercing / blunt (по умолчанию пусто = без типа)
/// attack_speed_modifier: множитель скорости атаки (1.0 = базовая, >1 = быстрее, <1 = медленнее)
/// </summary>
[Migration(15)]
public class AddWeaponDamageTypeAndSpeed : ForwardOnlyMigration
{
    public override void Up()
    {
        Execute.Sql("ALTER TABLE items ADD COLUMN damage_type TEXT DEFAULT '';");
        Execute.Sql("ALTER TABLE items ADD COLUMN attack_speed_modifier REAL DEFAULT 1.0;");
    }
}

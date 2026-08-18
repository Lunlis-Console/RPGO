using FluentMigrator;

namespace LostAndDivine.Shared.Migrations;

/// <summary>
/// Блок и парирование больше не растут от атрибутов — только от экипировки.
/// Бонусы шмота теперь трактуются как прямой процент (не «очки» в кривой убывающей отдачи),
/// вклад шмота капится на 5% (MaxBlockGearBonus / MaxParryGearBonus).
/// Щиты получают блок в процентах, кинжалы — парирование по качеству.
/// </summary>
[Migration(1058)]
public class BlockParryGearPercent : ForwardOnlyMigration
{
    public override void Up()
    {
        // Щиты: блок в процентах (деревянный — слабый, стальной — крепче)
        Update.Table("items")
            .Set(new { bonus_block_chance = 2.0 })
            .Where(new { id = "I0211" });
        Update.Table("items")
            .Set(new { bonus_block_chance = 3.0 })
            .Where(new { id = "I0212" });

        // Кинжалы: парирование по качеству (Необычный 1%, Редкий 2%, Эпический 3%)
        Execute.Sql("UPDATE items SET bonus_parry_chance = 1.0 WHERE weapon_subtype = 'dagger' AND description LIKE '%Качество: Необычный%'");
        Execute.Sql("UPDATE items SET bonus_parry_chance = 2.0 WHERE weapon_subtype = 'dagger' AND description LIKE '%Качество: Редкий%'");
        Execute.Sql("UPDATE items SET bonus_parry_chance = 3.0 WHERE weapon_subtype = 'dagger' AND description LIKE '%Качество: Эпический%'");
    }
}
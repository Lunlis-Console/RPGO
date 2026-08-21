using FluentMigrator;

namespace LostAndDivine.Shared.Migrations;

/// <summary>
/// Проставляет поле location для NPC «Кузнец» (N0004), копируя зону из Торговца (N0015).
/// Это чисто мета-поле (используется квестами/диалогами/редактором); на рендеринг не влияет —
/// Кузнец уже корректно привязан к зоне города через Tiled-объект в zone_main.tmj.
/// </summary>
[Migration(1072)]
public class FixBlacksmithLocation : ForwardOnlyMigration
{
    public override void Up()
    {
        // COALESCE гарантирует, что значение не будет NULL (колонка location NOT NULL).
        // На БД, где нет записи N0015, подзапрос вернул бы NULL и упал бы NOT NULL constraint.
        Execute.Sql("UPDATE npcs SET location = COALESCE((SELECT location FROM npcs WHERE id='N0015'), '') WHERE id='N0004' AND (location IS NULL OR location='')");
    }
}

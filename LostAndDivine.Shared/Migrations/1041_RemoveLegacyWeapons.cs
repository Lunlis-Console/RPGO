using FluentMigrator;

namespace LostAndDivine.Shared.Migrations;

/// <summary>
/// Удаление легаси-оружия (I0001–I0525) после сида новой системы W0001–W0615
/// и замена ассортимента торговца N0001 на новый каталог.
/// </summary>
[Migration(1041)]
public class RemoveLegacyWeapons : ForwardOnlyMigration
{
    public override void Up()
    {
        // Все легаси-предметы оружейных типов удаляются; снаряжение (glove/belt),
        // расходники (consumable) и новое оружие (W*) остаются.
        Execute.Sql("DELETE FROM items WHERE type IN ('weapon','twohand','shield') AND id NOT LIKE 'W%'");

        // Торговец: убрать ссылки на удалённые предметы (зелья и снаряжение остаются).
        Execute.Sql("DELETE FROM merchant_stock WHERE item_id NOT IN (SELECT id FROM items)");

        // Торговец: полный новый каталог оружия (как раньше — все типы и уровни).
        Execute.Sql("INSERT OR IGNORE INTO merchant_stock (npc_id, item_id) SELECT 'N0001', id FROM items WHERE id LIKE 'W%'");
    }
}

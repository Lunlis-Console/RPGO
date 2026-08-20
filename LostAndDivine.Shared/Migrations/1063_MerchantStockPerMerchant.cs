using FluentMigrator;

namespace LostAndDivine.Shared.Migrations;

/// <summary>
/// Запас в лавке становится per-мерчант: merchant_stock.stock — сколько единиц
/// каждого товара есть в наличии у конкретного торговца (задаётся в редакторе
/// ассортимента). Существующие строки получают запас из items.stock (legacy);
/// колонка items.stock больше не редактируется в редакторе предметов.
/// </summary>
[Migration(1063)]
public class MerchantStockPerMerchant : ForwardOnlyMigration
{
    public override void Up()
    {
        Alter.Table("merchant_stock").AddColumn("stock").AsInt32().WithDefaultValue(1).NotNullable();

        // Бэкфилл: переносим запас из items.stock в merchant_stock (минимум 1).
        Execute.Sql(@"UPDATE merchant_stock
            SET stock = (SELECT MAX(1, items.stock) FROM items WHERE items.id = merchant_stock.item_id)");
    }
}
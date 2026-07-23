using FluentMigrator;

namespace RPGGame.Shared.Migrations;

[Migration(27)]
public class AddShieldsToMerchant : ForwardOnlyMigration
{
    public override void Up()
    {
        Execute.Sql("INSERT OR IGNORE INTO merchant_stock (npc_id, item_id) VALUES ('N0001','I0211')");
        Execute.Sql("INSERT OR IGNORE INTO merchant_stock (npc_id, item_id) VALUES ('N0001','I0212')");
    }
}

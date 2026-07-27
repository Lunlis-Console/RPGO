using FluentMigrator;

namespace RPGGame.Shared.Migrations;

[Migration(1005)]
public class RemoveWands : ForwardOnlyMigration
{
    public override void Up()
    {
        Delete.FromTable("items").Row(new { id = "I0511" });
        Delete.FromTable("items").Row(new { id = "I0512" });
        Delete.FromTable("items").Row(new { id = "I0513" });
        Delete.FromTable("items").Row(new { id = "I0514" });
        Delete.FromTable("items").Row(new { id = "I0515" });
    }
}

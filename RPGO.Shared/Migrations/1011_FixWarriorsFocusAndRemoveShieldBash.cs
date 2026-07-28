using FluentMigrator;

namespace RPGGame.Shared.Migrations;

[Migration(1011)]
public class FixWarriorsFocusAndRemoveShieldBash : ForwardOnlyMigration
{
    public override void Up()
    {
        Delete.FromTable("skills").Row(new { id = "SK0005" });

        Update.Table("skills")
            .Set(new { type = "Пассивные", parent_id = "SK0003", tier = 2 })
            .Where(new { id = "SK0006" });
    }
}

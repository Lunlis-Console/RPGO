using FluentMigrator;

namespace LostAndDivine.Shared.Migrations;

[Migration(1012)]
public class UpdateStrongArmDescription : ForwardOnlyMigration
{
    public override void Up()
    {
        Update.Table("skills")
            .Set(new { description = "Увеличивает урон ближней атаки на 15%. 100% шанс прока оружия. 50% шанс оглушить цель на 3 секунды." })
            .Where(new { id = "SK0001" });
    }
}

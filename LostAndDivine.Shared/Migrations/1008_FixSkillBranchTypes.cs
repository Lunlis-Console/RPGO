using FluentMigrator;

namespace LostAndDivine.Shared.Migrations;

[Migration(1008)]
public class FixSkillBranchTypes : ForwardOnlyMigration
{
    public override void Up()
    {
        Update.Table("skills")
            .Set(new { type = "Активные" }).Where(new { id = "SK0001" });
        Update.Table("skills")
            .Set(new { type = "Активные" }).Where(new { id = "SK0002" });
        Update.Table("skills")
            .Set(new { type = "Пассивные" }).Where(new { id = "SK0003" });
    }
}

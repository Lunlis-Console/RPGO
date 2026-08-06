using FluentMigrator;

namespace LostAndDivine.Shared.Migrations;

[Migration(1025)]
public class UpdateSkillMinLevels : ForwardOnlyMigration
{
    public override void Up()
    {
        // Меч · Акт (Sword Active): T1=1, T2=10, T3=20, T4=30, T5=40
        Update.Table("skills").Set(new { min_level = 1 }).Where(new { id = "SK0001" });
        Update.Table("skills").Set(new { min_level = 10 }).Where(new { id = "SK0002" });
        Update.Table("skills").Set(new { min_level = 20 }).Where(new { id = "SK0004" });
        Update.Table("skills").Set(new { min_level = 30 }).Where(new { id = "SK0007" });
        Update.Table("skills").Set(new { min_level = 40 }).Where(new { id = "SK0009" });

        // Меч · Пас (Sword Passive): T1=5, T2=15, T3=25, T4=35, T5=45
        Update.Table("skills").Set(new { min_level = 5 }).Where(new { id = "SK0003" });
        Update.Table("skills").Set(new { min_level = 15 }).Where(new { id = "SK0006" });
        Update.Table("skills").Set(new { min_level = 25 }).Where(new { id = "SK0008" });
        Update.Table("skills").Set(new { min_level = 35 }).Where(new { id = "SK0010" });
        Update.Table("skills").Set(new { min_level = 45 }).Where(new { id = "SK0011" });

        // Лук · Пас (Bow Passive): T1=5, T2=15, T3=25, T4=35, T5=45
        Update.Table("skills").Set(new { min_level = 5 }).Where(new { id = "SK0017" });
        Update.Table("skills").Set(new { min_level = 15 }).Where(new { id = "SK0018" });
        Update.Table("skills").Set(new { min_level = 25 }).Where(new { id = "SK0019" });
        Update.Table("skills").Set(new { min_level = 35 }).Where(new { id = "SK0020" });
        Update.Table("skills").Set(new { min_level = 45 }).Where(new { id = "SK0021" });
    }
}

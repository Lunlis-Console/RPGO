using FluentMigrator;

namespace LostAndDivine.Shared.Migrations;

[Migration(1026)]
public class StandardizeSkillDescriptions : ForwardOnlyMigration
{
    public override void Up()
    {
        // ── Меч · Акт ──
        Update.Table("skills")
            .Set(new { description = "Увеличивает урон ближней атаки на 15%. Шанс оглушения — 50% (3 сек)." })
            .Where(new { id = "SK0001" });

        Update.Table("skills")
            .Set(new { description = "+30% к скорости атаки на 10 сек." })
            .Where(new { id = "SK0002" });

        Update.Table("skills")
            .Set(new { description = "Две атаки по 150% от базы. +20% к крит. урону." })
            .Where(new { id = "SK0004" });

        Update.Table("skills")
            .Set(new { description = "Три удара по 200% от базы. Шанс наложить стан, разоружение или контузию (3 сек)." })
            .Where(new { id = "SK0007" });

        Update.Table("skills")
            .Set(new { description = "6 ударов: 180% + 15% за каждый следующий. При смене цели — 700% + 35% за пропущенные удары и стан." })
            .Where(new { id = "SK0009" });

        // ── Меч · Пас ──
        Update.Table("skills")
            .Set(new { description = "+25% к урону левой руки при двойном оружии." })
            .Where(new { id = "SK0003" });

        Update.Table("skills")
            .Set(new { description = "+10% точности и крит. шанса по оглушённым целям." })
            .Where(new { id = "SK0006" });

        Update.Table("skills")
            .Set(new { description = "+10% к шансу парирования при двойном оружии." })
            .Where(new { id = "SK0008" });

        Update.Table("skills")
            .Set(new { description = "+10% вампиризма от урона мечом." })
            .Where(new { id = "SK0010" });

        Update.Table("skills")
            .Set(new { description = "+2% к урону за каждые 5% потерянного HP (до +40%)." })
            .Where(new { id = "SK0011" });

        // ── Лук · Акт ──
        Update.Table("skills")
            .Set(new { description = "Выстрел из лука: 125% от базы. Только с луком." })
            .Where(new { id = "SK0012" });

        Update.Table("skills")
            .Set(new { description = "Выстрел: 110% от базы. Гарантированный крит (PvE). Обездвиживание. Только с луком." })
            .Where(new { id = "SK0013" });

        Update.Table("skills")
            .Set(new { description = "Отход на 3 клетки + ловушка на 4 сек: дым (−40% точности), капкан (стан) или кислота (20% урона/сек)." })
            .Where(new { id = "SK0014" });

        Update.Table("skills")
            .Set(new { description = "Автоатаки конусом (~30°) на 60% от базы (10 сек). −12% к скорости атаки." })
            .Where(new { id = "SK0015" });

        Update.Table("skills")
            .Set(new { description = "Выстрел: 1200% от базы. Крит + игнор 30% защиты." })
            .Where(new { id = "SK0016" });

        // ── Лук · Пас ──
        Update.Table("skills")
            .Set(new { description = "+7% шанса выпустить доп. стрелу при автоатаке." })
            .Where(new { id = "SK0017" });

        Update.Table("skills")
            .Set(new { description = "+15% точности стрельбы." })
            .Where(new { id = "SK0018" });

        Update.Table("skills")
            .Set(new { description = "+15% к уклонению от атак ближнего боя." })
            .Where(new { id = "SK0019" });

        Update.Table("skills")
            .Set(new { description = "+1 к дальности атаки. Ближе 2 клеток — до +25% пробивания брони." })
            .Where(new { id = "SK0020" });

        Update.Table("skills")
            .Set(new { description = "+20% крит. шанса по целям со станом, замедлением или сниженной точностью." })
            .Where(new { id = "SK0021" });
    }
}

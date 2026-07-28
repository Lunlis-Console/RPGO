using FluentMigrator;

namespace RPGGame.Shared.Migrations;

[Migration(1016)]
public class AddDuelSkill : ForwardOnlyMigration
{
    public override void Up()
    {
        Insert.IntoTable("skills").Row(new
        {
            id = "SK0009",
            name = "ЭТО ДУЭЛЬ!",
            description = "Серия из 6 ударов: первый +180% от базы, каждый следующий +15% к урону. Если цель сменяет таргет с вас (переагр или PvP-игрок, переключившийся на другую цель) — урон +700% от базы плюс +35% за каждый ненанесённый удар из серии и оглушение цели с 100% шансом. Кулдаун 45 секунд. Стоимость маны 35.",
            type = "Активные",
            mp_cost = 35,
            cooldown_ms = 45000,
            damage_multiplier = 1.8,
            min_level = 1,
            skill_point_cost = 1,
            parent_id = "SK0007",
            tier = 5
        });
    }
}

using FluentMigrator;

namespace LostAndDivine.Shared.Migrations;

[Migration(1023)]
public class AddBowSkillPath : ForwardOnlyMigration
{
    public override void Up()
    {
        // Переименовать ветки меча для двухколоночного UI
        Update.Table("skills").Set(new { type = "Меч · Акт" }).Where(new { type = "Активные" });
        Update.Table("skills").Set(new { type = "Меч · Пас" }).Where(new { type = "Пассивные" });

        // ── Активные: Путь лука ──
        Insert.IntoTable("skills").Row(new
        {
            id = "SK0012",
            name = "Прицельный выстрел",
            description = "Меткий выстрел из лука. Урон 125% от базы. Только с луком.",
            type = "Лук · Акт",
            mp_cost = 5,
            cooldown_ms = 4000,
            damage_multiplier = 1.25,
            min_level = 1,
            skill_point_cost = 1,
            parent_id = (string?)null,
            tier = 1,
            cast_time_ms = 200,
            max_rank = 3
        });

        Insert.IntoTable("skills").Row(new
        {
            id = "SK0013",
            name = "Ахиллесова пята",
            description = "Урон 110%, PvE: гарантированный крит. Попадание накладывает Обездвижен (длительность растёт с рангом). Только с луком.",
            type = "Лук · Акт",
            mp_cost = 12,
            cooldown_ms = 12000,
            damage_multiplier = 1.10,
            min_level = 10,
            skill_point_cost = 1,
            parent_id = "SK0012",
            tier = 2,
            cast_time_ms = 300,
            max_rank = 3
        });

        Insert.IntoTable("skills").Row(new
        {
            id = "SK0014",
            name = "Отступление",
            description = "Лучник отскакивает на 3 клетки назад, оставляя ловушку на 4 сек: дымовую шашку (−40% точности), капкан (Обездвижен) или кислотную лужу (20% баз. урона/сек, игнор резистов, −10% скорости).",
            type = "Лук · Акт",
            mp_cost = 18,
            cooldown_ms = 16000,
            damage_multiplier = 1.0,
            min_level = 20,
            skill_point_cost = 1,
            parent_id = "SK0013",
            tier = 3,
            cast_time_ms = 0,
            max_rank = 3
        });

        Insert.IntoTable("skills").Row(new
        {
            id = "SK0015",
            name = "Подавляющий огонь",
            description = "На 10 сек лучник ведёт подавляющий огонь: автоатаки бьют конусом (~30°) на 60% от базы. −12% к скорости атаки.",
            type = "Лук · Акт",
            mp_cost = 22,
            cooldown_ms = 28000,
            damage_multiplier = 0.60,
            min_level = 30,
            skill_point_cost = 1,
            parent_id = "SK0014",
            tier = 4,
            cast_time_ms = 0,
            max_rank = 3
        });

        Insert.IntoTable("skills").Row(new
        {
            id = "SK0016",
            name = "Пришёл, увидел, победил",
            description = "Выстрел колоссальной силы: 1200% урона от базы. Считается попаданием в Уязвимое место (крит + игнор 30% защиты).",
            type = "Лук · Акт",
            mp_cost = 40,
            cooldown_ms = 45000,
            damage_multiplier = 12.0,
            min_level = 40,
            skill_point_cost = 1,
            parent_id = "SK0015",
            tier = 5,
            cast_time_ms = 800,
            max_rank = 3
        });

        // ── Пассивные: Путь лука ──
        Insert.IntoTable("skills").Row(new
        {
            id = "SK0017",
            name = "Вам подарочек",
            description = "7% шанс выпустить ещё одну стрелу при автоатаке из лука.",
            type = "Лук · Пас",
            mp_cost = 0,
            cooldown_ms = 0,
            damage_multiplier = 1.0,
            min_level = 1,
            skill_point_cost = 1,
            parent_id = "SK0012",
            tier = 1,
            cast_time_ms = 0,
            max_rank = 3
        });

        Insert.IntoTable("skills").Row(new
        {
            id = "SK0018",
            name = "Белке в глаз",
            description = "Повышает точность стрельбы на 15% (снижает шанс уклонения цели).",
            type = "Лук · Пас",
            mp_cost = 0,
            cooldown_ms = 0,
            damage_multiplier = 1.0,
            min_level = 10,
            skill_point_cost = 1,
            parent_id = "SK0017",
            tier = 2,
            cast_time_ms = 0,
            max_rank = 3
        });

        Insert.IntoTable("skills").Row(new
        {
            id = "SK0019",
            name = "Руками не трогать",
            description = "Увеличивает шанс уклониться от атак ближнего боя на 15%.",
            type = "Лук · Пас",
            mp_cost = 0,
            cooldown_ms = 0,
            damage_multiplier = 1.0,
            min_level = 20,
            skill_point_cost = 1,
            parent_id = "SK0018",
            tier = 3,
            cast_time_ms = 0,
            max_rank = 3
        });

        Insert.IntoTable("skills").Row(new
        {
            id = "SK0020",
            name = "Дальний прицел",
            description = "+1 к дальности атаки лука. Ближе к цели (дист. ≤2) — больше пробивания брони (до 25%).",
            type = "Лук · Пас",
            mp_cost = 0,
            cooldown_ms = 0,
            damage_multiplier = 1.0,
            min_level = 30,
            skill_point_cost = 1,
            parent_id = "SK0019",
            tier = 4,
            cast_time_ms = 0,
            max_rank = 3
        });

        Insert.IntoTable("skills").Row(new
        {
            id = "SK0021",
            name = "Охотничий инстинкт",
            description = "+20% шанс крита по целям с Обездвижен, Замедлением или сниженной точностью.",
            type = "Лук · Пас",
            mp_cost = 0,
            cooldown_ms = 0,
            damage_multiplier = 1.0,
            min_level = 40,
            skill_point_cost = 1,
            parent_id = "SK0020",
            tier = 5,
            cast_time_ms = 0,
            max_rank = 3
        });
    }
}

using FluentMigrator;

namespace LostAndDivine.Shared.Migrations;

/// <summary>
/// Выносливость теперь даёт +10 HP за вложенное очко (было +5).
/// Существующим персонажам добавляем недостающие +5 HP за каждое очко,
/// вложенное сверх базиса класса (вклад при создании уже был +10/очко).
/// </summary>
[Migration(1059)]
public class EnduranceHpTenPerPoint : ForwardOnlyMigration
{
    public override void Up()
    {
        // Базис выносливости по классам: Воин 3, Разбойник 1, Некромаг 2, Маг 1, Потрошитель 1, Колдун 1
        Execute.Sql("UPDATE characters SET max_health = max_health + MAX(0, endurance - 3) * 5 WHERE class = 0");
        Execute.Sql("UPDATE characters SET max_health = max_health + MAX(0, endurance - 1) * 5 WHERE class = 1");
        Execute.Sql("UPDATE characters SET max_health = max_health + MAX(0, endurance - 2) * 5 WHERE class = 2");
        Execute.Sql("UPDATE characters SET max_health = max_health + MAX(0, endurance - 1) * 5 WHERE class = 3");
        Execute.Sql("UPDATE characters SET max_health = max_health + MAX(0, endurance - 1) * 5 WHERE class = 4");
        Execute.Sql("UPDATE characters SET max_health = max_health + MAX(0, endurance - 1) * 5 WHERE class = 5");
    }
}
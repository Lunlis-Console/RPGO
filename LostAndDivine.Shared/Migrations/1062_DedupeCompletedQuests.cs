using FluentMigrator;

namespace LostAndDivine.Shared.Migrations;

/// <summary>
/// Дубли в player_completed_quests: таблица создавалась без UNIQUE-ограничения,
/// поэтому INSERT OR IGNORE не защищал от повторов (падал загрузчик персонажа
/// с «An item with the same key has already been added»). Удаляем дубли
/// (оставляем самую раннюю запись) и добавляем уникальный индекс.
/// </summary>
[Migration(1062)]
public class DedupeCompletedQuests : ForwardOnlyMigration
{
    public override void Up()
    {
        Execute.Sql(@"DELETE FROM player_completed_quests
            WHERE rowid NOT IN (
                SELECT MIN(rowid) FROM player_completed_quests GROUP BY player_name, quest_id
            )");

        Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS uq_player_completed_quests ON player_completed_quests (player_name, quest_id)");
    }
}
using Microsoft.Data.Sqlite;

namespace RPGGame.Shared;

/// <summary>
/// ВАЖНО: Default-контент теперь хранится в game.db, которая закоммичена в репозиторий.
/// Предметы, монстры, квесты, NPC, зоны, порталы создаются через Editor и сохраняются прямо в game.db.
/// DataSeeder больше не содержит хардкодных INSERT-ов.
/// Если нужно добавить новый дефолтный контент с обновлением кода — создай новую миграцию FluentMigrator с INSERT OR IGNORE.
/// </summary>
public static class DataSeeder
{
    public static void Seed(string connectionString)
    {
        // Все seed-данные теперь находятся в закоммиченной game.db.
        // Никаких хардкодных INSERT-ов больше нет.
    }
}

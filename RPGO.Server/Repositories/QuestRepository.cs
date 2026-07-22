using Microsoft.Data.Sqlite;
using RPGGame.Shared.Models;

namespace RPGGame.Server.Repositories;

internal static class QuestRepository
{
    internal static List<QuestDefinition> LoadDefinitions()
    {
        lock (Db.Lock)
        {
            var result = new List<QuestDefinition>();
            using var connection = Db.Open();
            var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT id, title, description, type, target_monster_id, target_item_id, target, xp_reward, gold_reward FROM quests_def";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new QuestDefinition
                {
                    Id = reader.GetString(0),
                    Title = reader.GetString(1),
                    Description = reader.GetString(2),
                    Type = reader.GetString(3),
                    TargetMonsterId = reader.GetString(4),
                    TargetItemId = reader.GetString(5),
                    Target = reader.GetInt32(6),
                    XpReward = reader.GetInt32(7),
                    GoldReward = reader.GetInt32(8),
                });
            }
            return result;
        }
    }

    internal static void Save(SqliteConnection connection, string playerName, List<QuestProgress> quests)
    {
        var delete = connection.CreateCommand();
        delete.CommandText = "DELETE FROM quests WHERE player_name = $name";
        delete.Parameters.AddWithValue("$name", playerName);
        delete.ExecuteNonQuery();

        foreach (var q in quests)
        {
            var insert = connection.CreateCommand();
            insert.CommandText = @"
                INSERT INTO quests (player_name, quest_id, current, completed)
                VALUES ($name, $qid, $cur, $comp)";
            insert.Parameters.AddWithValue("$name", playerName);
            insert.Parameters.AddWithValue("$qid", q.QuestId);
            insert.Parameters.AddWithValue("$cur", q.Current);
            insert.Parameters.AddWithValue("$comp", q.Completed ? 1 : 0);
            insert.ExecuteNonQuery();
        }
    }

    internal static List<QuestProgress> Load(SqliteConnection connection, string playerName)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT quest_id, current, completed FROM quests WHERE player_name = $name";
        cmd.Parameters.AddWithValue("$name", playerName);

        var list = new List<QuestProgress>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new QuestProgress
            {
                QuestId = reader.GetString(0),
                Current = reader.GetInt32(1),
                Completed = reader.GetInt32(2) != 0
            });
        }
        return list;
    }
}

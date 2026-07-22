using Microsoft.Data.Sqlite;
using RPGGame.Shared.Models;

namespace RPGGame.Server.Repositories;

internal static class MonsterRepository
{
    internal static List<MonsterTemplate> LoadAll()
    {
        lock (Db.Lock)
        {
            var result = new List<MonsterTemplate>();
            using var connection = Db.Open();
            var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT id, name, tier, health, xp_reward, gold_reward, symbol, strength, endurance, agility, cunning, intellect, wisdom, crit_chance, crit_damage, evade_chance FROM monsters";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new MonsterTemplate
                {
                    Id = reader.GetString(0),
                    Name = reader.GetString(1),
                    Tier = reader.GetInt32(2),
                    Health = reader.GetInt32(3),
                    XpReward = reader.GetInt32(4),
                    GoldReward = reader.GetInt32(5),
                    Symbol = reader.GetString(6).Length > 0 ? reader.GetString(6)[0] : 'M',
                    Strength = reader.GetInt32(7),
                    Endurance = reader.GetInt32(8),
                    Agility = reader.GetInt32(9),
                    Cunning = reader.GetInt32(10),
                    Intellect = reader.GetInt32(11),
                    Wisdom = reader.GetInt32(12),
                    CritChance = reader.GetDouble(13),
                    CritDamage = reader.GetDouble(14),
                    EvadeChance = reader.GetDouble(15),
                });
            }
            return result;
        }
    }
}

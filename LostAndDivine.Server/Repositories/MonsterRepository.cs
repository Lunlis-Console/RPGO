using Microsoft.Data.Sqlite;
using LostAndDivine.Shared.Models;

namespace LostAndDivine.Server.Repositories;

internal static class MonsterRepository
{
    internal static List<MonsterTemplate> LoadAll()
    {
        lock (Db.Lock)
        {
            var result = new List<MonsterTemplate>();
            using var connection = Db.OpenContent();
            var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT id, name, tier, health, xp_reward, gold_reward, gold_max, symbol, strength, endurance, agility, cunning, intellect, wisdom, crit_chance, crit_damage, evade_chance, block_chance, parry_chance, shield_defense FROM monsters";
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
                    GoldMax = reader.GetInt32(6),
                    Symbol = reader.GetString(7).Length > 0 ? reader.GetString(7)[0] : 'M',
                    Strength = reader.GetInt32(8),
                    Endurance = reader.GetInt32(9),
                    Agility = reader.GetInt32(10),
                    Cunning = reader.GetInt32(11),
                    Intellect = reader.GetInt32(12),
                    Wisdom = reader.GetInt32(13),
                    CritChance = reader.GetDouble(14),
                    CritDamage = reader.GetDouble(15),
                    EvadeChance = reader.GetDouble(16),
                    BlockChance = reader.IsDBNull(17) ? 0 : reader.GetDouble(17),
                    ParryChance = reader.IsDBNull(18) ? 0 : reader.GetDouble(18),
                    ShieldDefense = reader.IsDBNull(19) ? 0 : reader.GetInt32(19),
                });
            }
            return result;
        }
    }
}

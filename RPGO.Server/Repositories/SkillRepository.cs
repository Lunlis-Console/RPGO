using RPGGame.Shared.Models;

namespace RPGGame.Server.Repositories;

internal static class SkillRepository
{
    private static List<Skill>? _cache;

    internal static List<Skill> LoadAll()
    {
        if (_cache != null)
            return _cache;

        var result = new List<Skill>();
        using var connection = Db.Open();
        var cmd = connection.CreateCommand();
        cmd.CommandText = @"SELECT id, name, description, type, mp_cost, cooldown_ms, damage_multiplier, min_level, skill_point_cost, parent_id, tier
            FROM skills";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new Skill
            {
                Id = reader.GetString(0),
                Name = reader.GetString(1),
                Description = reader.GetString(2),
                Type = reader.GetString(3),
                MpCost = reader.GetInt32(4),
                CooldownMs = reader.GetInt32(5),
                DamageMultiplier = reader.GetDouble(6),
                MinLevel = reader.GetInt32(7),
                SkillPointCost = reader.IsDBNull(8) ? 1 : reader.GetInt32(8),
                ParentId = reader.IsDBNull(9) ? null : reader.GetString(9),
                Tier = reader.IsDBNull(10) ? 1 : reader.GetInt32(10)
            });
        }
        _cache = result;
        return result;
    }

    internal static Skill? GetById(string id)
        => LoadAll().FirstOrDefault(s => s.Id == id);
}

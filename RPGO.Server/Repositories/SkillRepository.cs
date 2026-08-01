using RPGGame.Shared.Models;

namespace RPGGame.Server.Repositories;

internal static class SkillRepository
{
    private static List<Skill>? _cache;

    private static readonly Dictionary<string, string> _iconMap = new()
    {
        ["SK0001"] = "icon_skill_stronghand",
        ["SK0002"] = "icon_skill_barrageofblows",
        ["SK0007"] = "icon_skill_holytrinity",
        ["SK0009"] = "icon_skill_duel",
        ["SK0004"] = "icon_skill_cutting",
        ["SK0003"] = "icon_skill_ambidexter",
        ["SK0006"] = "icon_skill_warriorsconcentration",
        ["SK0008"] = "icon_skill_reflexes",
        ["SK0010"] = "icon_skill_bloodletting",
        ["SK0011"] = "icon_skill_berserk",
        ["SK0012"] = "icon_skill_aimedshot",
        ["SK0013"] = "icon_skill_achillesheel",
        ["SK0014"] = "icon_skill_retreat",
        ["SK0015"] = "icon_skill_suppressingfire",
        ["SK0016"] = "icon_skill_venividivici",
        ["SK0017"] = "icon_skill_extraarrow",
        ["SK0018"] = "icon_skill_bowaccuracy",
        ["SK0019"] = "icon_skill_meleeevade",
        ["SK0020"] = "icon_skill_longrangesight",
        ["SK0021"] = "icon_skill_huntinginstinct",
    };

    internal static List<Skill> LoadAll()
    {
        if (_cache != null)
            return _cache;

        var result = new List<Skill>();
        using var connection = Db.OpenContent();
        var cmd = connection.CreateCommand();
        cmd.CommandText = @"SELECT id, name, description, type, mp_cost, cooldown_ms, damage_multiplier, min_level, skill_point_cost, parent_id, tier, cast_time_ms, max_rank
            FROM skills";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            string id = reader.GetString(0);
            result.Add(new Skill
            {
                Id = id,
                Name = reader.GetString(1),
                Description = reader.GetString(2),
                Type = reader.GetString(3),
                MpCost = reader.GetInt32(4),
                CooldownMs = reader.GetInt32(5),
                DamageMultiplier = reader.GetDouble(6),
                MinLevel = reader.GetInt32(7),
                SkillPointCost = reader.IsDBNull(8) ? 1 : reader.GetInt32(8),
                ParentId = reader.IsDBNull(9) ? null : reader.GetString(9),
                Tier = reader.IsDBNull(10) ? 1 : reader.GetInt32(10),
                CastTimeMs = reader.IsDBNull(11) ? 0 : reader.GetInt32(11),
                MaxRank = reader.IsDBNull(12) ? 3 : reader.GetInt32(12),
                IconName = _iconMap.TryGetValue(id, out var ic) ? ic : null
            });
        }
        _cache = result;
        return result;
    }

    internal static Skill? GetById(string id)
        => LoadAll().FirstOrDefault(s => s.Id == id);
}

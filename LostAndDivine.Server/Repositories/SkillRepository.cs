using LostAndDivine.Shared;
using LostAndDivine.Shared.Models;

namespace LostAndDivine.Server.Repositories;

internal static class SkillRepository
{
    private static List<Skill>? _cache;

    private static readonly Dictionary<string, string> _iconMap = new()
    {
        [SkillIds.StrongArm]       = "icon_skill_stronghand",
        [SkillIds.Flurry]          = "icon_skill_barrageofblows",
        [SkillIds.HolyTrinity]     = "icon_skill_holytrinity",
        [SkillIds.Duel]            = "icon_skill_duel",
        [SkillIds.Slash]           = "icon_skill_cutting",
        [SkillIds.Ambidextrous]    = "icon_skill_ambidexter",
        [SkillIds.WarriorsFocus]   = "icon_skill_warriorsconcentration",
        [SkillIds.Reflexes]        = "icon_skill_reflexes",
        [SkillIds.Bloodletting]    = "icon_skill_bloodletting",
        [SkillIds.Berserk]         = "icon_skill_berserk",
        [SkillIds.AimedShot]       = "icon_skill_aimedshot",
        [SkillIds.AchillesHeel]    = "icon_skill_achillesheel",
        [SkillIds.Retreat]         = "icon_skill_retreat",
        [SkillIds.SuppressingFire] = "icon_skill_suppressingfire",
        [SkillIds.VeniVidiVici]    = "icon_skill_venividivici",
        [SkillIds.ExtraArrow]      = "icon_skill_extraarrow",
        [SkillIds.BowAccuracy]     = "icon_skill_bowaccuracy",
        [SkillIds.MeleeEvade]      = "icon_skill_meleeevade",
        [SkillIds.LongRangeSight]  = "icon_skill_longrangesight",
        [SkillIds.HuntingInstinct] = "icon_skill_huntinginstinct",
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

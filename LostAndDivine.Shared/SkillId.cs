namespace LostAndDivine.Shared;

/// <summary>
/// Типобезопасный ID навыка (P2-2). Заменяет строковые "SK0001".
/// </summary>
public enum SkillId
{
    Unknown = 0,
    StrongArm = 1,          // SK0001
    Flurry = 2,             // SK0002
    Ambidextrous = 3,       // SK0003
    Slash = 4,              // SK0004
    ShieldBash = 5,         // SK0005 удалён
    WarriorsFocus = 6,      // SK0006
    HolyTrinity = 7,        // SK0007
    Reflexes = 8,           // SK0008
    Duel = 9,               // SK0009
    Bloodletting = 10,      // SK0010
    Berserk = 11,           // SK0011
    AimedShot = 12,         // SK0012
    AchillesHeel = 13,      // SK0013
    Retreat = 14,           // SK0014
    SuppressingFire = 15,   // SK0015
    VeniVidiVici = 16,      // SK0016
    ExtraArrow = 17,        // SK0017
    BowAccuracy = 18,       // SK0018
    MeleeEvade = 19,        // SK0019
    LongRangeSight = 20,    // SK0020
    HuntingInstinct = 21,   // SK0021
}

/// <summary>
/// Каталог навыков: маппинг SkillId ↔ строковый код "SKxxxx" для проводного формата.
/// </summary>
public static class SkillCatalog
{
    private static readonly Dictionary<SkillId, string> _toString = new()
    {
        [SkillId.StrongArm] = SkillIds.StrongArm,
        [SkillId.Flurry] = SkillIds.Flurry,
        [SkillId.Ambidextrous] = SkillIds.Ambidextrous,
        [SkillId.Slash] = SkillIds.Slash,
        [SkillId.ShieldBash] = SkillIds.ShieldBash,
        [SkillId.WarriorsFocus] = SkillIds.WarriorsFocus,
        [SkillId.HolyTrinity] = SkillIds.HolyTrinity,
        [SkillId.Reflexes] = SkillIds.Reflexes,
        [SkillId.Duel] = SkillIds.Duel,
        [SkillId.Bloodletting] = SkillIds.Bloodletting,
        [SkillId.Berserk] = SkillIds.Berserk,
        [SkillId.AimedShot] = SkillIds.AimedShot,
        [SkillId.AchillesHeel] = SkillIds.AchillesHeel,
        [SkillId.Retreat] = SkillIds.Retreat,
        [SkillId.SuppressingFire] = SkillIds.SuppressingFire,
        [SkillId.VeniVidiVici] = SkillIds.VeniVidiVici,
        [SkillId.ExtraArrow] = SkillIds.ExtraArrow,
        [SkillId.BowAccuracy] = SkillIds.BowAccuracy,
        [SkillId.MeleeEvade] = SkillIds.MeleeEvade,
        [SkillId.LongRangeSight] = SkillIds.LongRangeSight,
        [SkillId.HuntingInstinct] = SkillIds.HuntingInstinct,
    };
    private static readonly Dictionary<string, SkillId> _fromString = _toString.ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.OrdinalIgnoreCase);

    public static string ToCode(SkillId id) => _toString.TryGetValue(id, out var code) ? code : $"SK{(int)id:0000}";
    public static SkillId FromCode(string code) => _fromString.TryGetValue(code, out var id) ? id : SkillId.Unknown;
    public static bool TryParse(string code, out SkillId id) => _fromString.TryGetValue(code, out id);
}

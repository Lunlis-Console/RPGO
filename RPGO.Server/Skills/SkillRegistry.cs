using RPGGame.Server.Skills.Executors;
using RPGGame.Shared;

namespace RPGGame.Server.Skills;

public static class SkillRegistry
{
    private static readonly Dictionary<string, ISkillExecutor> _executors = new()
    {
        [SkillIds.StrongArm]       = new StrongArmExecutor(),
        [SkillIds.Flurry]          = new FlurryExecutor(),
        [SkillIds.Slash]           = new SlashExecutor(),
        [SkillIds.HolyTrinity]     = new HolyTrinityExecutor(),
        [SkillIds.Duel]            = new DuelExecutor(),
        [SkillIds.AimedShot]       = new AimedShotExecutor(),
        [SkillIds.AchillesHeel]    = new AchillesHeelExecutor(),
        [SkillIds.Retreat]         = new RetreatExecutor(),
        [SkillIds.SuppressingFire] = new SuppressingFireExecutor(),
        [SkillIds.VeniVidiVici]    = new VeniVidiViciExecutor(),
    };

    public static ISkillExecutor? Get(string skillId)
        => _executors.TryGetValue(skillId, out var e) ? e : null;

    public static void Register(string skillId, ISkillExecutor executor)
        => _executors[skillId] = executor;
}

using RPGGame.Server.Skills.Executors;

namespace RPGGame.Server.Skills;

public static class SkillRegistry
{
    private static readonly Dictionary<string, ISkillExecutor> _executors = new()
    {
        ["SK0001"] = new StrongArmExecutor(),
        ["SK0002"] = new FlurryExecutor(),
        ["SK0004"] = new SlashExecutor(),
        ["SK0007"] = new HolyTrinityExecutor(),
        ["SK0009"] = new DuelExecutor(),
    };

    public static ISkillExecutor? Get(string skillId)
        => _executors.TryGetValue(skillId, out var e) ? e : null;

    public static void Register(string skillId, ISkillExecutor executor)
        => _executors[skillId] = executor;
}

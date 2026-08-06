namespace LostAndDivine.Server.Dependencies;

public interface ICombatInfra
{
    MonsterManager Monsters { get; }
    KillService KillService { get; }
    DebuffManager Debuffs { get; }
    ProjectileManager Projectiles { get; }
    CombatService Combat { get; }
    PvPService PvP { get; }
    HazardService Hazard { get; }
    PlayerDeathService PlayerDeath { get; }
    PartyManager Party { get; }
    CorpseManager Corpses { get; }
    LootManager Loot { get; }
}

using RPGGame.Server.Instances;
using RPGGame.Server.Network;
using RPGGame.Server.Services;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;

namespace RPGGame.Server;

public interface IGameServices
{
    GameWorld World { get; }
    INetworkHub Hub { get; }
    MonsterManager Monsters { get; }
    LootManager Loot { get; }
    CorpseManager Corpses { get; }
    QuestManager Quests { get; }
    MerchantManager Merchant { get; }
    CollectibleManager Collectibles { get; }
    TradeManager Trade { get; }
    DialogueManager Dialogue { get; }
    PartyManager Party { get; }
    ProjectileManager Projectiles { get; }
    KillService KillService { get; }
    PathfindingService Pathfinding { get; }
    DebuffManager Debuffs { get; }
    CombatService Combat { get; }
    PvPService PvP { get; }
    HazardService Hazard { get; }
    InteractionService Interactions { get; }
    AuthService Auth { get; }
    ZoneManager Zones { get; }
    InstanceManager Instances { get; }
    PersistenceService Persistence { get; }
    ClientBuildService ClientBuild { get; }
    StorageService Storage { get; }
    PlayerDeathService PlayerDeath { get; }

    Task ChatTo(ClientConnection? conn, ChatChannel channel, string name, string text);
    Task ChatToC(ClientConnection? conn, string name, string text);
    Task ReloadContent(ClientConnection? connection = null);
}

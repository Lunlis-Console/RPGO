using RPGGame.Server.Network;

namespace RPGGame.Server;

/// <summary>
/// Единый контейнер всех сервисов сервера. Создаётся один раз в Program.Main()
/// и передаётся всем компонентам через конструктор.
/// Заменяет статические синглтоны Program.World, Program.Hub и менеджеры.
/// </summary>
public sealed class GameServices
{
    public GameWorld World { get; }
    public INetworkHub Hub { get; }
    public MonsterManager Monsters { get; }
    public LootManager Loot { get; }
    public CorpseManager Corpses { get; }
    public QuestManager Quests { get; }
    public MerchantManager Merchant { get; }
    public CollectibleManager Collectibles { get; }
    public TradeManager Trade { get; }
    public DialogueManager Dialogue { get; }
    public PartyManager Party { get; }
    public ProjectileManager Projectiles { get; }
    public KillService KillService { get; }
    public PathfindingService Pathfinding { get; }
    public DebuffManager Debuffs { get; }

    public GameServices(
        GameWorld world,
        INetworkHub hub,
        MonsterManager monsters,
        LootManager loot,
        CorpseManager corpses,
        QuestManager quests,
        MerchantManager merchant,
        CollectibleManager collectibles,
        TradeManager trade,
        DialogueManager dialogue,
        PartyManager party,
        ProjectileManager projectiles,
        KillService killService,
        PathfindingService pathfinding,
        DebuffManager debuffs)
    {
        World = world;
        Hub = hub;
        Monsters = monsters;
        Loot = loot;
        Corpses = corpses;
        Quests = quests;
        Merchant = merchant;
        Collectibles = collectibles;
        Trade = trade;
        Dialogue = dialogue;
        Party = party;
        Projectiles = projectiles;
        KillService = killService;
        Pathfinding = pathfinding;
        Debuffs = debuffs;
    }
}

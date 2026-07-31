using RPGGame.Server.Instances;
using RPGGame.Server.Network;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;

namespace RPGGame.Server;

/// <summary>
/// Единый контейнер всех сервисов сервера. Создаётся один раз в Program.Main()
/// и передаётся всем компонентам через конструктор.
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
    public CombatService Combat { get; }
    public PvPService PvP { get; }
    public HazardService Hazard { get; }
    public InteractionService Interactions { get; }
    public AuthService Auth { get; }
    public ZoneManager Zones { get; }
    public InstanceManager Instances { get; }
    public PersistenceService Persistence { get; }

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
        DebuffManager debuffs,
        CombatService combat,
        PvPService pvp,
        HazardService hazard,
        InteractionService interactions,
        AuthService auth,
        ZoneManager zones,
        InstanceManager instances,
        PersistenceService persistence)
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
        Combat = combat;
        PvP = pvp;
        Hazard = hazard;
        Interactions = interactions;
        Auth = auth;
        Zones = zones;
        Instances = instances;
        Persistence = persistence;
    }

    public async Task ReloadContent(ClientConnection? connection = null)
    {
        try
        {
            Log.Info("Перезагрузка данных на сервере...");
            Merchant.Initialize();
            Quests.Initialize();
            Dialogue.LoadAll();
            Loot.LoadFromDatabase();
            Monsters.Initialize();
            Collectibles.Initialize();
            Hub.LoadNpcCache();

            await Hub.BroadcastChatAsync("Система", "Данные обновлены (предметы, диалоги, квесты, монстры).");

            if (connection != null)
            {
                await Hub.SendToClient(connection, new GameMessage
                {
                    Type = "chat",
                    Data = new { Name = "Система", Text = "Обновление завершено." }
                });
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Ошибка обновления: {ex.Message}", ex);
            if (connection != null)
            {
                await Hub.SendToClient(connection, new GameMessage
                {
                    Type = "chat",
                    Data = new { Name = "Система", Text = "Ошибка обновления: " + ex.Message }
                });
            }
        }
    }
}

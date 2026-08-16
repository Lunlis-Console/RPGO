using LostAndDivine.Server.Instances;
using LostAndDivine.Server.MessageHandlers;
using LostAndDivine.Server.Network;
using LostAndDivine.Server.Services;
using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;

namespace LostAndDivine.Server;

/// <summary>
/// Единый контейнер всех сервисов сервера. Создаётся один раз в Program.Main()
/// и передаётся всем компонентам через конструктор.
/// </summary>
public sealed class GameServices : IGameServices
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
    public CombatService Combat { get; set; } = null!;
    public PvPService PvP { get; set; } = null!;
    public HazardService Hazard { get; set; } = null!;
    public InteractionService Interactions { get; set; } = null!;
    public AuthService Auth { get; set; } = null!;
    public ZoneManager Zones { get; }
    public InstanceManager Instances { get; set; } = null!;
    public PersistenceService Persistence { get; }
    public ClientBuildService ClientBuild { get; }
    public StorageService Storage { get; }

    // Спавн-точки из Tiled-карт: переиспользуются при повторной инициализации (/reload),
    // чтобы монстры и собираемые предметы появлялись так же, как при старте сервера.
    private List<TiledSpawn>? _monsterSpawns;
    private readonly Dictionary<string, List<TiledSpawn>> _collectibleSpawns = new(StringComparer.OrdinalIgnoreCase);

    public void SetSpawnData(List<TiledSpawn>? monsterSpawns, Dictionary<string, List<TiledSpawn>> collectibleSpawns)
    {
        _monsterSpawns = monsterSpawns;
        _collectibleSpawns.Clear();
        foreach (var (zoneId, list) in collectibleSpawns)
            _collectibleSpawns[zoneId] = list;
    }
    public PlayerDeathService PlayerDeath { get; set; } = null!;
    public MonsterCombatCalculator MonsterCombat { get; set; } = null!;
    public MonsterAttackService MonsterAttacks { get; set; } = null!;
    public MessageHandlerRegistry MessageHandlers { get; }

    public Task ChatTo(ClientConnection? conn, ChatChannel channel, string name, string text)
    {
        if (conn == null) return Task.CompletedTask;
        return Hub.SendChatToAsync(conn, channel, name, text);
    }

    public Task ChatToC(ClientConnection? conn, string name, string text)
        => ChatTo(conn, ChatChannel.Combat, name, text);

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
        AuthService auth,
        ZoneManager zones,
        PersistenceService persistence,
        ClientBuildService clientBuild,
        StorageService storage)
    {
        World = world;
        pathfinding.Services = this;
        Hub = hub;
        Monsters = monsters;
        Loot = loot;
        Corpses = corpses;
        Quests = quests;
        quests.NpcLookup = (zoneId, npcId) => hub.FindNpcById(zoneId, npcId);
        Merchant = merchant;
        Collectibles = collectibles;
        Trade = trade;
        Dialogue = dialogue;
        Party = party;
        Projectiles = projectiles;
        KillService = killService;
        Pathfinding = pathfinding;
        Debuffs = debuffs;
        Auth = auth;
        Zones = zones;
        Persistence = persistence;
        ClientBuild = clientBuild;
        Storage = storage;
        MessageHandlers = new MessageHandlerRegistry();
    }

    public async Task ReloadContent(ClientConnection? connection = null)
    {
        try
        {
            Log.Info("Перезагрузка данных на сервере...");
            Merchant.Initialize();
            Quests.Initialize();
            Quests.ReloadQuestItems();
            Dialogue.LoadAll();
            Loot.LoadFromDatabase();
            Monsters.Initialize(_monsterSpawns);
            foreach (var (zoneId, spawns) in _collectibleSpawns)
                Collectibles.Initialize(spawns, zoneId);
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

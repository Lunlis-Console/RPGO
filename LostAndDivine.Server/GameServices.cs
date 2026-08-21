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
    public SectorWorld Sectors { get; }
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
        SectorWorld sectors,
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
        Sectors = sectors;
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
            LostAndDivine.Server.Repositories.SkillRepository.InvalidateCache();
            LostAndDivine.Server.Repositories.InventoryRepository.InvalidateTemplateCache();
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

    /// <summary>
    /// Перезагружает секторы открытого мира с диска (Content/Sectors/*.tmj) и заново
    /// встраивает их в карту мира: тайлы, препятствия, NPC, порталы, двери и точки спавна.
    /// Монстры и коллекционки пересоздаются из свежих точек, склад переставляется.
    /// Онлайн-игрокам повторно отправляются секторы вокруг их позиций — карту можно
    /// рисовать в Tiled и смотреть результат без перезапуска сервера/клиента.
    /// </summary>
    public async Task ReloadSectors(ClientConnection? connection = null)
    {
        try
        {
            Log.Info("Перезагрузка секторов открытого мира...");
            LostAndDivine.Server.Repositories.SkillRepository.InvalidateCache();
            LostAndDivine.Server.Repositories.InventoryRepository.InvalidateTemplateCache();
            var sectorsDir = ContentPaths.SectorsDir;
            if (!Directory.Exists(sectorsDir))
                throw new InvalidOperationException($"Папка секторов не найдена: {sectorsDir}");

            Zones.ClearTiledRegistrations(Balance.MainZoneId);
            World.Map.ClearObstacles();
            Sectors.Load(World.Map, Zones, sectorsDir);

            // Спавн-данные: секторы дают main-зону, остальные зоны сохраняют свои точки.
            var allSpawns = new List<TiledSpawn>(Sectors.AllSpawns);
            var allCollectibleSpawns = new Dictionary<string, List<TiledSpawn>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (zoneId, spawns) in Sectors.AllCollectibleSpawns)
                allCollectibleSpawns[zoneId] = spawns;
            foreach (var (zoneId, spawns) in _collectibleSpawns)
            {
                if (string.Equals(zoneId, Balance.MainZoneId, StringComparison.OrdinalIgnoreCase)) continue;
                allSpawns.AddRange(spawns);
                allCollectibleSpawns[zoneId] = spawns;
            }
            SetSpawnData(allSpawns, allCollectibleSpawns);

            // Позиции мерчанта/доски/манекенов из свежих NPC-точек секторов.
            Monsters.ResetMannequinPositions();
            var tiledNpcs = Zones.GetAllTiledNpcs();
            var merchantTiled = tiledNpcs.FirstOrDefault(n => string.Equals(n.Type, "merchant", StringComparison.OrdinalIgnoreCase));
            if (merchantTiled != null) Merchant.SetTiledPosition(merchantTiled.X, merchantTiled.Y);
            var boardTiled = tiledNpcs.FirstOrDefault(n => string.Equals(n.Type, "board", StringComparison.OrdinalIgnoreCase));
            if (boardTiled != null) Quests.SetTiledPosition(boardTiled.X, boardTiled.Y);
            foreach (var dummyTiled in tiledNpcs.Where(n => string.Equals(n.Type, "dummy", StringComparison.OrdinalIgnoreCase)))
                Monsters.AddMannequinPosition(dummyTiled.X, dummyTiled.Y);

            Merchant.Initialize();
            Quests.Initialize();
            Monsters.Initialize(allSpawns.Count > 0 ? allSpawns : null);
            foreach (var (zoneId, spawns) in allCollectibleSpawns)
                Collectibles.Initialize(spawns, zoneId);

            RelocateStorage();

            Hub.LoadNpcCache();

            // Сначала сообщаем клиентам о перезагрузке (они сбрасывают кэш карты мира
            // и запрашивают полный слепок), и только потом рассылаем свежие секторы
            // вокруг игроков. В обратном порядке клиент принял бы 3x3 до сброса, а после
            // сброса повторные запросы этих секторов сервер бы проигнорировал
            // (дедупликация HasSectorSent) — карта застревала бы на 501/510.
            await Hub.SendToAllAsync(new GameMessage { Type = "sectors_reloaded", Data = new { } });

            // Онлайн-игрокам повторно шлём свежие секторы вокруг их позиций.
            foreach (var conn in World.GetAllConnectionsSnapshot())
            {
                if (conn.Player == null) continue;
                conn.ResetSectorsSent();
                if (conn.Player.CurrentZoneId == Balance.MainZoneId)
                    await Hub.SendSectorsAround(conn, conn.Player);
            }

            await Hub.BroadcastChatAsync("Система", "Секторы открытого мира обновлены (карта, NPC, порталы, спавны).");
            if (connection != null)
            {
                await Hub.SendToClient(connection, new GameMessage
                {
                    Type = "chat",
                    Data = new { Name = "Система", Text = "Секторы обновлены." }
                });
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Ошибка перезагрузки секторов: {ex.Message}", ex);
            if (connection != null)
            {
                await Hub.SendToClient(connection, new GameMessage
                {
                    Type = "chat",
                    Data = new { Name = "Система", Text = "Ошибка перезагрузки секторов: " + ex.Message }
                });
            }
        }
    }

    /// <summary>Размещает склад рядом с мерчантом и добавляет его клетку как препятствие карты мира.</summary>
    private void RelocateStorage()
    {
        int storageX = Merchant.MerchantX + 1;
        int storageY = Merchant.MerchantY;
        if (World.Map.IsObstacle(storageX, storageY))
        {
            int[] dx = { 0, 0, -1, 1, 1, -1, 1, -1 };
            int[] dy = { -1, 1, 0, 0, -1, -1, 1, 1 };
            storageX = Merchant.MerchantX;
            storageY = Merchant.MerchantY;
            for (int i = 0; i < 8; i++)
            {
                int nx = Merchant.MerchantX + dx[i];
                int ny = Merchant.MerchantY + dy[i];
                if (!World.Map.IsObstacle(nx, ny))
                {
                    storageX = nx;
                    storageY = ny;
                    break;
                }
            }
        }
        World.Map.AddObstacle(storageX, storageY);
        Storage.SetPosition(storageX, storageY);
        Log.Info($"Склад размещён на ({storageX}, {storageY})");
    }
}

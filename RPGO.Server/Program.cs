using RPGGame.Server.Instances;
using RPGGame.Server.Network;
using RPGGame.Server.MessageHandlers;
using RPGGame.Server.Services;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace RPGGame.Server;

partial class Program
{
    public static GameServices Services { get; internal set; } = null!;
    private static GameServerHost? _host;

    public static double GetAttackSpeed(Player player)
        => Balance.GetAttackSpeedWithWeapon(player.Agility, player.Equipment.GetWeaponSpeedModifier());

    static async Task Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.InputEncoding = System.Text.Encoding.UTF8;

        Log.Init();

        Log.Info("Инициализация базы данных...");
        DatabaseManager.Initialize();
        DatabaseManager.CreateTestAccountIfNeeded();

        Log.Info("Загрузка манифеста клиента (обновления)...");
        var clientBuild = new ClientBuildService();
        clientBuild.Initialize();

        Log.Info("Создание игрового мира...");
        var world = new GameWorld(Balance.WorldWidth, Balance.WorldHeight);

        // Создаём менеджеры (порядок важен для зависимостей)
        var monsters = new MonsterManager(world);
        var loot = new LootManager(world);
        var corpses = new CorpseManager();
        var quests = new QuestManager(world);
        var merchant = new MerchantManager(world);
        var collectibles = new CollectibleManager(world);
        var trade = new TradeManager();
        var party = new PartyManager(world);
        var debuffs = new DebuffManager();
        var killService = new KillService(world);
        var projectiles = new ProjectileManager(world);
        var dialogue = new DialogueManager(world, quests, merchant);
        var pathfinding = new PathfindingService(world, merchant, quests);
        var zones = new ZoneManager();
        zones.SetMainMap(world.Map); // main-зона = карта мира (тайлы + препятствия)

        Log.Info("Загрузка данных (предметы, квесты, зоны)...");
        loot.LoadFromDatabase();
        zones.LoadAll();

        Log.Info("Загрузка Tiled-карт...");
        List<TiledSpawn>? spawns = null;
        try
        {
            spawns = LoadTiledZone(zones, "wordlmap.tmj", "main");
            LoadTiledZone(zones, "arena_1.tmj", "arena");
        }
        catch (Exception ex)
        {
            Log.Error("Ошибка загрузки Tiled-карт", ex);
        }

        // Позиции торговца, доски заданий и манекена берутся из Tiled-карт (контент — из БД)
        var tiledNpcs = zones.GetAllTiledNpcs();
        var merchantTiled = tiledNpcs.FirstOrDefault(n => string.Equals(n.Type, "merchant", StringComparison.OrdinalIgnoreCase));
        if (merchantTiled != null) merchant.SetTiledPosition(merchantTiled.X, merchantTiled.Y);
        var boardTiled = tiledNpcs.FirstOrDefault(n => string.Equals(n.Type, "board", StringComparison.OrdinalIgnoreCase));
        if (boardTiled != null) quests.SetTiledPosition(boardTiled.X, boardTiled.Y);
        foreach (var dummyTiled in tiledNpcs.Where(n => string.Equals(n.Type, "dummy", StringComparison.OrdinalIgnoreCase)))
            monsters.AddMannequinPosition(dummyTiled.X, dummyTiled.Y);

        merchant.Initialize();
        quests.Initialize();
        dialogue.LoadAll();

        Log.Info("Загрузка монстров...");
        monsters.Initialize(spawns);
        collectibles.Initialize(spawns);

        // Создаём сетевой хаб
        var hub = new GameServer(world);

        // Связываем циклические зависимости
        killService.SetHub(hub);
        projectiles.SetHub(hub);
        dialogue.SetHub(hub);
        party.SetHub(hub);
        world.SetDependencies(hub, player => { Services.Persistence.EnqueueSave(player); return true; });

        // Lazy<GameServices> для разрыва циклических зависимостей:
        // CombatService, InteractionService, AuthService, InstanceManager
        // получают Lazy и резолвят GameServices при первом обращении.
        var servicesLazy = new Lazy<GameServices>(() => Services);

        // Создаём сервисы с циклическими зависимостями
        var combat = new CombatService(servicesLazy);
        var pvp = new PvPService(servicesLazy);
        var hazard = new HazardService(servicesLazy);
        var interactions = new InteractionService(servicesLazy);
        var auth = new AuthService(servicesLazy);
        var instances = new InstanceManager(servicesLazy);
        instances.LoadAll();
        instances.ApplyTiledPortals(zones.GetAllTiledNpcs());
        var persistence = new PersistenceService();

        // Единственное создание GameServices
        Services = new GameServices(world, hub, monsters, loot, corpses, quests, merchant, collectibles,
            trade, dialogue, party, projectiles, killService, pathfinding, debuffs,
            combat, pvp, hazard, interactions, auth, zones, instances, persistence, clientBuild);

        hub.SetServices(Services);
        monsters.SetServices(Services);
        dialogue.SetServices(Services);
        projectiles.SetServices(Services);
        killService.SetGameServices(Services);

        MessageHandlerRegistry.RegisterAll(Services);
        hub.LoadNpcCache();
        persistence.Start();

        // Запуск фоновых задач
        _host = new GameServerHost(Services);
        _ = Task.Run(() => _host.StartAsync());

        TcpListener server = new TcpListener(IPAddress.Any, Balance.ServerPort);
        server.Start();

        Log.Info($"Сервер запущен на порту {Balance.ServerPort}");
        Log.Info($"Дата: {DateTime.Now}");
        Log.Info($"Карта: {Balance.WorldWidth}x{Balance.WorldHeight}");
        Log.Info($"Игроков: {DatabaseManager.GetAccountCount()}");
        Log.Info("IP адреса для подключения:");
        foreach (var ip in GetLocalIPs())
            Log.Info($"  {ip}");
        Log.Info("Ожидание подключения...");

        while (true)
        {
            TcpClient client = await server.AcceptTcpClientAsync();
            Log.Info($"Подключение клиента: {client.Client.RemoteEndPoint}");

            ClientConnection connection = new ClientConnection(client);
            world.AddClient(connection);

            _ = Task.Run(() => HandleClientAsync(connection));
        }
    }

    private static async Task HandleClientAsync(ClientConnection connection)
    {
        Player? player = null;
        bool authenticated = false;

        try
        {
            Stream stream = connection.Client.GetStream();

            while (!authenticated)
            {
                GameMessage? message = await NetworkHelper.ReceiveAsync<GameMessage>(stream);
                if (message == null)
                {
                    Log.Info($"Отключение клиента: {connection.Endpoint}");
                    return;
                }

                if (await Services.ClientBuild.HandleUnauthenticatedAsync(connection, message, Services.Hub))
                    continue;

                authenticated = await Services.Auth.HandleAuthMessage(connection, message, Services.Hub);
            }

            while (true)
            {
                GameMessage? message = await NetworkHelper.ReceiveAsync<GameMessage>(stream);
                if (message == null)
                {
                    Log.Info($"Отключение клиента: {connection.Endpoint}");
                    break;
                }

                player = await ProcessMessage(connection, message, player ?? connection.Player);
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Ошибка: {ex.Message}", ex);
        }
        finally
        {
            if (player != null)
            {
                var tradeSession = Services.Trade.GetSession(player.Id);
                if (tradeSession != null) Services.Trade.CancelSession(tradeSession, "отключение клиента");
                player.IsTrading = false;

                // Удаляем игрока из инстанса, если он там
                Services.Instances.RemovePlayer(player);

                Services.World.RemovePlayer(player);
                Services.World.RemoveClient(connection);
                Log.Info($"Игрок {player.Name} покинул игру");
                await Services.Hub.BroadcastMapAsync();

                Services.Persistence.EnqueueSave(player);
            }

            try { connection.Client.Close(); } catch (Exception ex) { Log.Warn($"Close client: {ex.Message}"); }
        }
    }

    private static async Task<Player?> ProcessMessage(ClientConnection connection, GameMessage message, Player? player)
    {
        try
        {
            if (message.Type is "register" or "login_auth")
            {
                await Services.Auth.HandleAuthMessage(connection, message, Services.Hub);
                return player;
            }

            if (MessageHandlerRegistry.TryGet(message.Type, out var handler))
            {
                await handler.Handle(connection, message, player);
                return player;
            }

            Log.Warn($"Неизвестный тип сообщения: {message.Type}");
        }
        catch (Exception ex)
        {
            Log.Error($"Ошибка обработки {message.Type}", ex);
        }

        return player;
    }

    public static async Task ReloadContent(ClientConnection? connection = null)
    {
        try
        {
            Log.Info("Перезагрузка данных на сервере...");
            Services.Merchant.Initialize();
            Services.Quests.Initialize();
            Services.Dialogue.LoadAll();
            Services.Loot.LoadFromDatabase();
            Services.Monsters.Initialize();
            Services.Collectibles.Initialize();
            Services.Hub.LoadNpcCache();

            await Services.Hub.BroadcastChatAsync("Система", "Данные обновлены (предметы, диалоги, квесты, монстры).");

            if (connection != null)
            {
                await Services.Hub.SendToClient(connection, new GameMessage
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
                await Services.Hub.SendToClient(connection, new GameMessage
                {
                    Type = "chat",
                    Data = new { Name = "Система", Text = "Ошибка обновления: " + ex.Message }
                });
            }
        }
    }

    internal static async Task ChatTo(ClientConnection? conn, ChatChannel channel, string name, string text)
    {
        if (conn == null) return;
        await Services.Hub.SendChatToAsync(conn, channel, name, text);
    }

    /// <summary>
    /// Загружает Tiled-карту (Content/{fileName}) и применяет её к зоне:
    /// тайлы, препятствия, тайл-конфигурация и размер карты зоны.
    /// </summary>
    private static List<TiledSpawn>? LoadTiledZone(ZoneManager zones, string fileName, string zoneId)
    {
        string tiledPath = Path.Combine(AppContext.BaseDirectory, "Content", fileName);
        if (!File.Exists(tiledPath))
        {
            Log.Warn($"Tiled-карта не найдена: {tiledPath}");
            return null;
        }

        var tiledMap = TiledMapLoader.Load(tiledPath);
        var tileData = TiledMapLoader.ExtractTileLayer(tiledMap);
        var gameMap = zones.CreateOrReplaceMap(zoneId, tiledMap.Width, tiledMap.Height);
        gameMap.SetTiles(tileData);

        var obstacles = TiledMapLoader.ExtractObstacles(tiledMap);
        foreach (var (ox, oy) in obstacles)
            gameMap.AddObstacle(ox, oy);

        string tilesetId = tiledMap.Tilesets.Count > 0 ? tiledMap.Tilesets[0].Name : zoneId;
        var objectLayer = TiledMapLoader.ExtractObjectLayer(tiledMap);
        var objectTileset = TiledMapLoader.GetObjectLayerTileset(tiledMap);
        zones.SetTileConfig(zoneId, tiledMap.TileWidth, tilesetId,
            objectTileset?.Name, objectTileset?.TileWidth ?? 0);
        if (objectLayer != null)
            gameMap.SetObjectTiles(objectLayer);

        var spawns = TiledMapLoader.ExtractSpawns(tiledMap);

        var tiledNpcs = TiledMapLoader.ExtractNpcs(tiledMap, zoneId);
        if (tiledNpcs.Count > 0)
            zones.RegisterTiledNpcs(zoneId, tiledNpcs);

        var tiledPortals = TiledMapLoader.ExtractPortals(tiledMap, toZone =>
        {
            var targetZone = zones.GetZone(toZone);
            return targetZone != null ? ((int X, int Y)?)(targetZone.SpawnX, targetZone.SpawnY) : null;
        });
        if (tiledPortals.Count > 0)
        {
            zones.RegisterTiledPortals(tiledPortals.Select(p => new WorldPortal
            {
                Id = $"tiled_{zoneId}_{p.X}_{p.Y}",
                FromZone = zoneId,
                FromX = p.X,
                FromY = p.Y,
                ToZone = p.ToZone,
                ToX = p.ToX,
                ToY = p.ToY
            }));
        }

        Log.Info($"Tiled-карта {fileName} загружена в зону '{zoneId}': {tiledMap.Width}x{tiledMap.Height}, тайлов: {tileData.Length}, препятствий: {obstacles.Count}, точек спавна: {spawns.Count}, порталов: {tiledPortals.Count}");
        return spawns;
    }

    private static List<string> GetLocalIPs()
    {
        var ips = new List<string> { "127.0.0.1 (localhost)" };
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var ip in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                        ips.Add(ip.Address.ToString());
                }
            }
        }
        catch { }
        return ips;
    }
}

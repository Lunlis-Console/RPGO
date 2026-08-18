using LostAndDivine.Server.Instances;
using LostAndDivine.Server.Network;
using LostAndDivine.Server.MessageHandlers;
using LostAndDivine.Server.Services;
using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
 
namespace LostAndDivine.Server;

partial class Program
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleOutputCP(uint codePage);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleCP(uint codePage);
    public static GameServices Services { get; internal set; } = null!;
    private static GameServerHost? _host;
    private static TestBot? _testBot;
    private static readonly object _botLock = new();
    private static readonly ConnectionGuard _connectionGuard = new();

    public static double GetAttackSpeed(Player player)
        => Balance.GetAttackSpeedWithWeapon(player.GetAttackSpeedPoints(), player.Equipment.GetWeaponSpeedModifier());

    static async Task Main(string[] args)
    {
        if (OperatingSystem.IsWindows())
        {
            SetConsoleOutputCP(65001);
            SetConsoleCP(65001);
        }
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
        var world = new GameWorld(Balance.MainWorldWidth, Balance.MainWorldHeight);

        // Базовые сервисы (создаются раньше зависимостей)
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
        zones.SetMainMap(world.Map); // main-зона = основная карта (тайлы + препятствия)

        Log.Info("Загрузка сервисов (монстры, квесты, лут)...");
        loot.LoadFromDatabase();
        zones.LoadAll();

        // Секторный открытый мир (main): тайлы/препятствия/NPC/порталы/спавны из
        // Content/Sectors/{col}_{row}.tmj встраиваются в карту мира (3000x1700).
        var sectorWorld = new SectorWorld();
        var contentDir = ContentPaths.ContentDir;
        var sectorsDir = ContentPaths.SectorsDir;
        Log.Info($"Папка контента: {contentDir}");
        try
        {
            if (Directory.Exists(sectorsDir))
                sectorWorld.Load(world.Map, zones, sectorsDir);
            else
                Log.Warn($"Папка секторов не найдена: {sectorsDir} — открытый мир без тайлов");
        }
        catch (Exception ex)
        {
            Log.Error("Ошибка загрузки секторов открытого мира", ex);
        }

        Log.Info("Загрузка Tiled-карт...");
        var allSpawns = new List<TiledSpawn>();
        var allCollectibleSpawns = new Dictionary<string, List<TiledSpawn>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            // Правило имён: zone_{id}.tmj в зону, dungeon_*.tmj в общие подземелья.
            // *_text.tmj в режиме вспомогательной карты игнорируются.
            // zone_main больше не загружается: его контент перенесён в сектор 3_7.
            foreach (var file in Directory.GetFiles(contentDir, "zone_*.tmj", SearchOption.TopDirectoryOnly))
            {
                string fname = Path.GetFileName(file);
                string zoneId = Path.GetFileNameWithoutExtension(fname).Substring("zone_".Length);

                if (zoneId == Balance.MainZoneId) continue;

                var zoneSpawns = LoadTiledZone(zones, fname, zoneId);
                if (zoneSpawns == null) continue;

                allSpawns.AddRange(zoneSpawns);
                if (!allCollectibleSpawns.ContainsKey(zoneId))
                    allCollectibleSpawns[zoneId] = new List<TiledSpawn>();
                allCollectibleSpawns[zoneId].AddRange(zoneSpawns);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Ошибка загрузки Tiled-карт", ex);
        }

        // Спавны секторов открытого мира (глобальные координаты)
        if (sectorWorld.AllSpawns.Count > 0)
            allSpawns.AddRange(sectorWorld.AllSpawns);
        foreach (var (zoneId, zoneSpawns) in sectorWorld.AllCollectibleSpawns)
        {
            if (!allCollectibleSpawns.ContainsKey(zoneId))
                allCollectibleSpawns[zoneId] = new List<TiledSpawn>();
            allCollectibleSpawns[zoneId].AddRange(zoneSpawns);
        }

        // Мёртвые торговцы, чтобы квесты и предметы мерчанта из Tiled-карт (работает и с ИИ)
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

        Log.Info("Инициализация монстров...");
        var spawns = allSpawns.Count > 0 ? allSpawns : null;
        monsters.Initialize(spawns);
        foreach (var (zoneId, zoneCollectSpawns) in allCollectibleSpawns)
            collectibles.Initialize(zoneCollectSpawns, zoneId);

        // Собираем ядро сети
        var hub = new GameServer(world);
        var persistence = new PersistenceService();
        var storage = new StorageService(world, hub);

        // GameServices собирает по крупицам сервисы воедино
        Services = new GameServices(world, hub, sectorWorld, monsters, loot, corpses, quests, merchant, collectibles,
            trade, dialogue, party, projectiles, killService, pathfinding, debuffs,
            auth: null!, zones: zones, persistence, clientBuild, storage);

        // Спавн-точки Tiled-карт сохраняются для корректного /reload
        Services.SetSpawnData(spawns, allCollectibleSpawns);

        // Внедряем зависимости, насыщаем GameServices
        killService.SetHub(hub);
        projectiles.SetHub(hub);
        dialogue.SetHub(hub);
        party.SetHub(hub);
        world.SetDependencies(hub, player => { Services.Persistence.EnqueueSave(player); return true; });

        // Циклы с рекурсивной зависимостью: GameServices сам себя,
        // поэтому IGameServices сделано через Lazy<>
        var combat = new CombatService(Services);
        var pvp = new PvPService(Services);
        var hazard = new HazardService(Services);
        var interactions = new InteractionService(Services);
        var playerDeath = new PlayerDeathService(Services);
        var monsterCombat = new MonsterCombatCalculator(Services);
        var monsterAttacks = new MonsterAttackService(Services);
        var auth = new AuthService(Services);
        var instances = new InstanceManager(Services);
        instances.LoadAll();
        instances.ApplyTiledPortals(zones.GetAllTiledNpcs());

        // GameServices собирает по крупицам сервисы воедино
        Services.Combat = combat;
        Services.PvP = pvp;
        Services.Hazard = hazard;
        Services.Interactions = interactions;
        Services.PlayerDeath = playerDeath;
        Services.MonsterCombat = monsterCombat;
        Services.MonsterAttacks = monsterAttacks;
        Services.Instances = instances;
        Services.Auth = auth;

        // Подгружаем карты всех подземелий для быстрой загрузки
        var dungeonFiles = Directory.GetFiles(contentDir, "dungeon_*.tmj", SearchOption.TopDirectoryOnly);
        if (dungeonFiles.Length > 0)
        {
            var dungeonFile = dungeonFiles[0];
            var dungeonTemplate = LoadTiledMap(Path.GetFileName(dungeonFile));
            if (dungeonTemplate != null)
            {
                var tiledData = TiledMapLoader.Load(dungeonFile);
                var dungeonSpawns = TiledMapLoader.ExtractDungeonObjects(tiledData);
                instances.SetDungeonTemplate(dungeonTemplate, dungeonSpawns);
            }
        }

        // Установка сундука склада в локации
        int storageX = merchant.MerchantX + 1;
        int storageY = merchant.MerchantY;
        if (world.Map.IsObstacle(storageX, storageY))
        {
            // Если позиция склада занята в локации
            int[] dx = { 0, 0, -1, 1, 1, -1, 1, -1 };
            int[] dy = { -1, 1, 0, 0, -1, -1, 1, 1 };
            storageX = merchant.MerchantX;
            storageY = merchant.MerchantY;
            for (int i = 0; i < 8; i++)
            {
                int nx = merchant.MerchantX + dx[i];
                int ny = merchant.MerchantY + dy[i];
                if (!world.Map.IsObstacle(nx, ny))
                {
                    storageX = nx;
                    storageY = ny;
                    break;
                }
            }
        }
        world.Map.AddObstacle(storageX, storageY);
        storage.SetPosition(storageX, storageY);
        Log.Info($"Склад размещён на ({storageX}, {storageY})");

        hub.SetServices(Services);
        monsters.SetServices(Services);
        dialogue.SetServices(Services);
        projectiles.SetServices(Services);
        killService.SetGameServices(Services);

        Services.MessageHandlers.RegisterAll(Services);
        hub.LoadNpcCache();
        persistence.Start();

        // Heartbeat-сервис: эмитирует keep-alive (~60с) и убивает
        // зомби-соединения (пропуск 3 ping = 15с таймаут).
        var heartbeat = new HeartbeatHandler(world, hub, persistence);
        _ = heartbeat.StartAsync(CancellationToken.None);

        // Graceful shutdown: ловим сигналы для гашения сервера
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            ShutdownServer();
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            // Последний шанс: принудительно сбрасываем очередь на диск
            try { Services?.Persistence.FlushNow(); } catch { }
        };

        // Запуск игрового мира
        _host = new GameServerHost(Services);
        _ = Task.Run(() => _host.StartAsync());

        TcpListener server = new TcpListener(IPAddress.Any, Balance.ServerPort);
        server.Start();

        Log.Info($"Сервер запущен на порту {Balance.ServerPort}");
        Log.Info($"Дата: {DateTime.Now}");
        Log.Info($"Карта: {Balance.MainWorldWidth}x{Balance.MainWorldHeight}");
        Log.Info($"Игроков: {DatabaseManager.GetAccountCount()}");
        Log.Info("IP адреса для подключения:");
        foreach (var ip in GetLocalIPs())
            Log.Info($"  {ip}");
        Log.Info("Ожидание подключений...");

        // Консоль сервера: команды из stdin (чат, боты, остановка и т.д.)
        if (args.Any(a => a.Equals("--bot", StringComparison.OrdinalIgnoreCase)))
        {
            StartTestBot();
        }
        if (args.Any(a => a.Equals("--reload", StringComparison.OrdinalIgnoreCase)))
        {
            Log.Info("Флаг --reload: перезагрузка данных после старта...");
            try { await Services.ReloadContent(); }
            catch (Exception ex) { Log.Error($"Ошибка при --reload: {ex.Message}", ex); }
        }
        _ = Task.Run(() => ServerConsoleLoop());

        while (true)
        {
            TcpClient client = await server.AcceptTcpClientAsync();
            string ip = ConnectionGuard.NormalizeIp(client.Client.RemoteEndPoint?.ToString() ?? "");
            if (!_connectionGuard.Allow(ip))
            {
                Log.Debug($"Отклонено подключение (лимит/бан): {ip}");
                try { client.Close(); } catch { }
                continue;
            }

            Log.Debug($"TCP-подключение: {client.Client.RemoteEndPoint}");

            ClientConnection connection = new ClientConnection(client);
            world.AddClient(connection);

            _ = Task.Run(() => HandleClientAsync(connection));
        }
    }

    private static async Task HandleClientAsync(ClientConnection connection)
    {
        Player? player = null;
        bool authenticated = false;
        int messages = 0;

        try
        {
            Stream stream = connection.Client.GetStream();
            connection.Client.ReceiveTimeout = 15000;

            while (!authenticated)
            {
                GameMessage? message = await NetworkHelper.ReceiveAsync<GameMessage>(stream);
                if (message == null)
                {
                    Log.Debug($"Отключение клиента: {connection.Endpoint}");
                    return;
                }

                messages++;
                if (messages == 1)
                    Log.Info($"Клиент подключился: {connection.Endpoint}");

                if (await Services.ClientBuild.HandleUnauthenticatedAsync(connection, message, Services.Hub))
                    continue;

                // Аутентификация клиента на подключение: ReconnectHandler
                // Аутентифицирует сессию в том состоянии как и сохраняли.
                if (message.Type == "reconnect")
                {
                    if (Services.MessageHandlers.TryGet("reconnect", out var reconnectHandler))
                        await reconnectHandler.Handle(connection, message, null);
                    authenticated = connection.Player != null;
                    continue;
                }

                authenticated = await Services.Auth.HandleAuthMessage(connection, message, Services.Hub);
            }

            while (true)
            {
                GameMessage? message = await NetworkHelper.ReceiveAsync<GameMessage>(stream);
                if (message == null)
                {
                    Log.Debug($"Отключение клиента: {connection.Endpoint}");
                    break;
                }

                messages++;
                player = await ProcessMessage(connection, message, player ?? connection.Player);
            }
        }
        catch (Exception ex)
        {
            if (player == null && messages == 0)
                Log.Debug($"Мусорное подключение отброшено: {connection.Endpoint} ({ex.Message})");
            else
                Log.Error($"Ошибка: {ex.Message}", ex);
        }
        finally
        {
            if (player != null)
            {
                _connectionGuard.RecordSuccess(ConnectionGuard.NormalizeIp(connection.Endpoint));
                var tradeSession = Services.Trade.GetSession(player.Id);
                if (tradeSession != null) Services.Trade.CancelSession(tradeSession, "Отключение клиента");
                player.IsTrading = false;

                bool stillInWorld = Services.World.TryGetPlayerByName(player.Name, out var wp)
                    && ReferenceEquals(wp, player);
                if (stillInWorld)
                {
                    // Логик отключения: игрок вышел из мира (вылетел, упал, убился),
                    // ждём переподключения канала для сессии восстания.
                    // ждём переподключения канала для сессии восстания.
                    Services.World.MarkPendingReconnect(player);
                    Services.World.RemoveClient(connection);
                    Log.Info($"Игрок {player.Name} отключился (ждём переподключения канала)");
                }
                else
                {
                    // Однако: LogoutHandler не ставил галку на мир и плавно гасит.
                    await Services.Party.LeavePartyAsync(player);
                    Services.Instances.RemovePlayer(player);
                    Services.Persistence.EnqueueSave(player);
                    Log.Info($"Игрок {player.Name} вышел из мира (logout)");
                    await Services.Hub.BroadcastMapAsync();
                }
            }
            else if (messages == 0)
            {
                _connectionGuard.RecordFailure(ConnectionGuard.NormalizeIp(connection.Endpoint));
            }

            try { connection.Client.Close(); } catch (Exception ex) { Log.Warn($"Close client: {ex.Message}"); }
        }
    }

    private static async Task<Player?> ProcessMessage(ClientConnection connection, GameMessage message, Player? player)
    {
        try
        {
            if (message.Type is "register" or "login_auth" or "character_select" or "character_create" or "character_delete")
            {
                var isAuth = await Services.Auth.HandleAuthMessage(connection, message, Services.Hub);
                if (isAuth)
                    player = connection.Player;
                return player;
            }

            if (Services.MessageHandlers.TryGet(message.Type, out var handler))
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

    /// <summary>
    /// Боты и отладочный игровой мир (тестовый игрок). Загружаются при
    /// запуске с --bot, либо из консоли командой 'bot start'.
    /// </summary>
    private static void StartTestBot()
    {
        lock (_botLock)
        {
            if (_testBot != null)
            {
                Log.Warn("Тестовый бот уже запущен.");
                return;
            }

            var bot = new TestBot("127.0.0.1", Balance.ServerPort, "test", "123", "Бот");
            _testBot = bot;
            _ = Task.Run(() => bot.StartAsync());
            Log.Info("Тестовый бот подключился, логин: test / 123");
        }
    }

    /// <summary>
    /// Серверная консоль: читает команды из stdin (пока Serilog пишет в stdout и
    /// сам не блокируется) и выполняет их.
    /// </summary>
    private static async Task ServerConsoleLoop()
    {
        Log.Info("Консоль сервера: введите 'help' для списка команд.");

        if (!ConsoleManager.IsInteractiveConsole())
        {
            // Штат/Канал неинтерактивен (например, запущен из скрипта) и используем
            // Штат/Канал неинтерактивен (например, запущен из скрипта) и используем
            // простой ReadLine, без приставок истории.
            while (true)
            {
                string? line;
                try { line = await Task.Run(() => Console.ReadLine()); }
                catch { break; }
                if (line == null) break;

                line = line.Trim();
                if (line.Length == 0) continue;

                try { await HandleServerCommand(line); }
                catch (Exception ex) { Log.Error($"[Console] Ошибка: {ex.Message}", ex); }
            }
            return;
        }

        ConsoleManager.InputActive = true;

        var buffer = new StringBuilder();
        while (true)
        {
            ConsoleKeyInfo key;
            try { key = await Task.Run(() => Console.ReadKey(true)); }
            catch (InvalidOperationException) { break; }
            catch { break; }

            if (key.Key == ConsoleKey.Enter)
            {
                ConsoleManager.SetInput("");
                ConsoleManager.RenderInput();
                string line = buffer.ToString().Trim();
                buffer.Clear();

                if (line.Length == 0) continue;
                try { await HandleServerCommand(line); }
                catch (Exception ex) { Log.Error($"[Console] Ошибка: {ex.Message}", ex); }
            }
            else if (key.Key == ConsoleKey.Backspace)
            {
                if (buffer.Length > 0)
                {
                    buffer.Remove(buffer.Length - 1, 1);
                    ConsoleManager.SetInput(buffer.ToString());
                    ConsoleManager.RenderInput();
                }
            }
            else if (key.Key == ConsoleKey.Escape)
            {
                buffer.Clear();
                ConsoleManager.SetInput("");
                ConsoleManager.RenderInput();
            }
            else if (!char.IsControl(key.KeyChar))
            {
                buffer.Append(key.KeyChar);
                ConsoleManager.SetInput(buffer.ToString());
                ConsoleManager.RenderInput();
            }
        }
    }

    private static async Task HandleServerCommand(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string cmd = parts[0].ToLowerInvariant();

        switch (cmd)
        {
            case "help":
                Log.Info("Онлайн: пусто");
                Log.Info("  players              - список онлайн-игроков");
                Log.Info("  reload               - перезагрузить контент (монстры, коллекционки, квесты, лут)");
                Log.Info("Консоль сервера: введите 'help' для списка команд.");
                Log.Info("  bot start / bot stop - запустить/остановить бота на сервер");
                Log.Info("  stop                 - остановить сервер");
                break;

            case "players":
                var online = Services.World.GetPlayersSnapshot();
                if (online.Count == 0)
                {
                    Log.Info("Онлайн: пусто");
                }
                else
                {
                    var desc = string.Join(", ", online.Select(p => $"{p.Name} (уровень {p.Level})"));
                    Log.Info($"Онлайн ({online.Count}): {desc}");
                }
                break;

            case "reload":
                await Services.ReloadContent();
                break;

            case "bot":
                string sub = line.Length > 4 ? line.Substring(4).Trim() : "";
                if (sub.Equals("start", StringComparison.OrdinalIgnoreCase))
                {
                    StartTestBot();
                    return;
                }
                if (sub.Equals("stop", StringComparison.OrdinalIgnoreCase))
                {
                    TestBot? current;
                    lock (_botLock) { current = _testBot; _testBot = null; }
                    if (current == null)
                    {
                        Log.Warn("Тестовый бот не запущен.");
                        return;
                    }
                    current.Stop();
                    Log.Info("Тестовый бот остановлен.");
                    return;
                }
                if (sub.Length == 0 || sub.Equals("help", StringComparison.OrdinalIgnoreCase))
                {
                    Log.Info("Команды бота (тестовый игрок):");
                    Log.Info("  bot start                      - запустить бота (тестовый логин)");
                    Log.Info("  bot stop                       - остановить бота");
                    Log.Info("  bot say <текст>                - сказать в глобальный чат");
                    Log.Info("  bot whisper <игрок> <текст>    - личное сообщение");
                    Log.Info("  bot invite <игрок>             - пригласить в группу");
                    Log.Info("  bot leave                      - выйти из группы");
                    Log.Info("  bot trade <игрок>              - предложить обмен");
                    Log.Info("  bot trade_cancel               - отменить обмен");
                    Log.Info("  bot mail <игрок> <тема>        - отправить письмо");
                    Log.Info("    [-- <tid>x<количество> ...]  - с вложенными предметами (напр. -- I0002x2 I0501x1)");
                    Log.Info("  bot move <x> <y>               - телепортироваться");
                    Log.Info("  bot logout                     - выход из мира");
                    Log.Info("  (набирается в чате и прочих окнах без помех взаимодействия)");
                    return;
                }

                TestBot? bot;
                lock (_botLock) { bot = _testBot; }
                if (bot == null)
                {
                    Log.Warn("Тестовый бот не запущен. Введите 'bot start'");
                    return;
                }
                if (!bot.IsConnected)
                {
                    Log.Warn("Бот не подключён к серверу.");
                    return;
                }
                bot.EnqueueCommand(sub);
                break;

            case "stop":
            case "exit":
            case "quit":
                ShutdownServer();
                break;

            default:
                Log.Warn($"Неизвестная команда: {cmd}. Введите 'help'");
                break;
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Грациозный выход: сохраняем всех онлайн-игроков и вызволяем
    /// наружные сервисы плавно слиться с локаций.
    /// </summary>
    private static void ShutdownServer()
    {
        try
        {
            Log.Info("  (набирается в чате и прочих окнах без помех взаимодействия)");
            foreach (var conn in Services.World.GetAllConnectionsSnapshot())
            {
                if (conn.Player == null) continue;
                try { Services.Persistence.EnqueueSave(conn.Player); }
                catch (Exception ex) { Log.Warn($"Ошибка сохранения {conn.Player.Name} при выходе: {ex.Message}"); }
            }
            _host?.Stop();
            Services.Persistence.Stop();
            Log.Info("Сервер остановлен. До свидания!");
        }
        catch (Exception ex)
        {
            Log.Error("Ошибка при остановке сервера", ex);
        }
        Environment.Exit(0);
    }

    /// <summary>
    /// Загружаем Tiled-карту (Content/{fileName}) и встраиваем её в зону:
    /// плитки, препятствия, зоны-перемещения и точки спавна мобов.
    /// Если зоны нет в зонах-перемещениях и создаём её сами.
    /// </summary>
    private static List<TiledSpawn>? LoadTiledZone(ZoneManager zones, string fileName, string zoneId)
    {
        string tiledPath = Path.Combine(ContentPaths.ContentDir, fileName);
        if (!File.Exists(tiledPath))
        {
            Log.Warn($"Tiled-карта не найдена: {tiledPath}");
            return null;
        }

        var tiledMap = TiledMapLoader.Load(tiledPath);

        // Авто-регистрация зоны, если её нет в списке
        if (zoneId != Balance.MainZoneId && zones.GetZone(zoneId) == null)
        {
            zones.RegisterZone(zoneId, tiledMap.Width, tiledMap.Height);
            Log.Info($"Зона '{zoneId}' авто-зарегистрирована: {tiledMap.Width}x{tiledMap.Height}");
        }

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
            // Портал в открытый мир: координаты в свойствах заданы в старой локальной
            // системе zone_main → переводим в глобальные секторного мира.
            zones.RegisterTiledPortals(tiledPortals.Select(p => new WorldPortal
            {
                Id = $"tiled_{zoneId}_{p.X}_{p.Y}",
                FromZone = zoneId,
                FromX = p.X,
                FromY = p.Y,
                ToZone = p.ToZone,
                ToX = string.Equals(p.ToZone, Balance.MainZoneId, StringComparison.OrdinalIgnoreCase)
                    ? p.ToX + Balance.EntrySectorOffsetX : p.ToX,
                ToY = string.Equals(p.ToZone, Balance.MainZoneId, StringComparison.OrdinalIgnoreCase)
                    ? p.ToY + Balance.EntrySectorOffsetY : p.ToY
            }));
        }

        Log.Info($"Tiled-карта {fileName} загружена в зону '{zoneId}': {tiledMap.Width}x{tiledMap.Height}, плиток: {tileData.Length}, препятствий: {obstacles.Count}, точек спавна: {spawns.Count}, порталов: {tiledPortals.Count}");

        var tiledDoors = TiledMapLoader.ExtractDoors(tiledMap);
        if (tiledDoors.Count > 0)
        {
            zones.RegisterDoors(zoneId, tiledDoors);
            // Дверь — непреодолимая пешком преграда: путь через неё невозможен,
            // пройти можно только через взаимодействие (телепорт на клетку за дверью).
            foreach (var door in tiledDoors)
            {
                if (!gameMap.IsObstacle(door.X, door.Y))
                    gameMap.AddObstacle(door.X, door.Y);
            }
            Log.Info($"Дверей в зоне '{zoneId}': {tiledDoors.Count}");
        }

        var playerSpawn = TiledMapLoader.ExtractPlayerSpawn(tiledMap);
        if (playerSpawn != null)
        {
            var z = zones.GetZone(zoneId);
            if (z != null)
            {
                z.SpawnX = playerSpawn.Value.X;
                z.SpawnY = playerSpawn.Value.Y;
                Log.Info($"Точка спавна игрока для зоны '{zoneId}' установлена: ({z.SpawnX}, {z.SpawnY})");
            }
        }

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

    /// <summary>
    /// Загружаем Tiled-карту как standalone GameMap (без привязки к зоне).
    /// </summary>
    private static GameMap? LoadTiledMap(string fileName)
    {
        string path = Path.Combine(ContentPaths.ContentDir, fileName);
        if (!File.Exists(path))
        {
            Log.Warn($"Tiled-карта не найдена: {path}");
            return null;
        }
        var tiledMap = TiledMapLoader.Load(path);
        var tileData = TiledMapLoader.ExtractTileLayer(tiledMap);
        var map = new GameMap(tiledMap.Width, tiledMap.Height);
        map.SetTiles(tileData);
        var obstacles = TiledMapLoader.ExtractObstacles(tiledMap);
        foreach (var (ox, oy) in obstacles)
            map.AddObstacle(ox, oy);
        var objectLayer = TiledMapLoader.ExtractObjectLayer(tiledMap);
        if (objectLayer != null)
            map.SetObjectTiles(objectLayer);
        Log.Info($"Карта {fileName} загружена: {map.Width}x{map.Height}, препятствий: {obstacles.Count}");
        return map;
    }
}

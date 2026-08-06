using System.Collections.Concurrent;
using LostAndDivine.Server.Instances;
using LostAndDivine.Server.Network;
using LostAndDivine.Server.MessageHandlers;
using LostAndDivine.Server.Services;
using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace LostAndDivine.Server;

partial class Program
{
    public static GameServices Services { get; internal set; } = null!;
    private static GameServerHost? _host;
    private static TestBot? _testBot;
    private static readonly object _botLock = new();
    private static readonly ConcurrentDictionary<string, DateTime> _lastConnectTime = new();
    private static readonly TimeSpan ConnectThrottle = TimeSpan.FromSeconds(10);

    public static double GetAttackSpeed(Player player)
        => Balance.GetAttackSpeedWithWeapon(player.Agility, player.Equipment.GetWeaponSpeedModifier());

    static async Task Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.InputEncoding = System.Text.Encoding.UTF8;

        Log.Init();

        Log.Info("������������� ���� ������...");
        DatabaseManager.Initialize();
        DatabaseManager.CreateTestAccountIfNeeded();

        Log.Info("�������� ��������� ������� (����������)...");
        var clientBuild = new ClientBuildService();
        clientBuild.Initialize();

        Log.Info("�������� �������� ����...");
        var world = new GameWorld(Balance.WorldWidth, Balance.WorldHeight);

        // ������ ��������� (������� ����� ��� ������������)
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
        zones.SetMainMap(world.Map); // main-���� = ����� ���� (����� + �����������)

        Log.Info("�������� ������ (��������, ������, ����)...");
        loot.LoadFromDatabase();
        zones.LoadAll();

        Log.Info("�������� Tiled-����...");
        var contentDir = Path.Combine(AppContext.BaseDirectory, "Content");
        var allSpawns = new List<TiledSpawn>();
        var allCollectibleSpawns = new Dictionary<string, List<TiledSpawn>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            // ��������� ���: zone_{id}.tmj � ����, dungeon_*.tmj � ������� ���������.
            // *_text.tmj � ������ ��������������� ����� ������������.
            foreach (var file in Directory.GetFiles(contentDir, "zone_*.tmj", SearchOption.TopDirectoryOnly))
            {
                string fname = Path.GetFileName(file);
                string zoneId = Path.GetFileNameWithoutExtension(fname).Substring("zone_".Length);

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
            Log.Error("������ �������� Tiled-����", ex);
        }

        // ������� ��������, ����� ������� � �������� ������� �� Tiled-���� (������� � �� ��)
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

        Log.Info("�������� ��������...");
        var spawns = allSpawns.Count > 0 ? allSpawns : null;
        monsters.Initialize(spawns);
        foreach (var (zoneId, zoneCollectSpawns) in allCollectibleSpawns)
            collectibles.Initialize(zoneCollectSpawns, zoneId);

        // ������ ������� ���
        var hub = new GameServer(world);
        var persistence = new PersistenceService();
        var storage = new StorageService(world, hub);

        // GameServices �������� �� ���������� ��������� ��������
        Services = new GameServices(world, hub, monsters, loot, corpses, quests, merchant, collectibles,
            trade, dialogue, party, projectiles, killService, pathfinding, debuffs,
            auth: null!, zones: zones, persistence, clientBuild, storage);

        // ��������� �����������, ��������� GameServices
        killService.SetHub(hub);
        projectiles.SetHub(hub);
        dialogue.SetHub(hub);
        party.SetHub(hub);
        world.SetDependencies(hub, player => { Services.Persistence.EnqueueSave(player); return true; });

        // ������� � ������������ �������������: GameServices ��� ������,
        // ������� IGameServices �������� � ��� Lazy<>
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

        // ������������� ����������� ������� �� GameServices
        Services.Combat = combat;
        Services.PvP = pvp;
        Services.Hazard = hazard;
        Services.Interactions = interactions;
        Services.PlayerDeath = playerDeath;
        Services.MonsterCombat = monsterCombat;
        Services.MonsterAttacks = monsterAttacks;
        Services.Instances = instances;
        Services.Auth = auth;

        // ��������� ������ ����� ���������� ��� ������ ��� ���������
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

        // ��������� ������� ������ ����� � ���������
        int storageX = merchant.MerchantX + 1;
        int storageY = merchant.MerchantY;
        if (world.Map.IsObstacle(storageX, storageY))
        {
            // ���� ��������� ������ ����� � ���������
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
        Log.Info($"����� �������� �� ({storageX}, {storageY})");

        hub.SetServices(Services);
        monsters.SetServices(Services);
        dialogue.SetServices(Services);
        projectiles.SetServices(Services);
        killService.SetGameServices(Services);

        Services.MessageHandlers.RegisterAll(Services);
        hub.LoadNpcCache();
        persistence.Start();

        // Heartbeat-������: �������� ������-������� (~60�) � ����������
        // �������� ���������� (������� 3 ping = 15� �������).
        var heartbeat = new HeartbeatHandler(world, hub, persistence);
        _ = heartbeat.StartAsync(CancellationToken.None);

        // Graceful shutdown: ��������� �������� ��� ��������� �������
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            ShutdownServer();
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            // ��������� �����: ���������� ������������� ������� ��� ������ ��������
            try { Services?.Persistence.FlushNow(); } catch { }
        };

        // ������ ������� �����
        _host = new GameServerHost(Services);
        _ = Task.Run(() => _host.StartAsync());

        TcpListener server = new TcpListener(IPAddress.Any, Balance.ServerPort);
        server.Start();

        Log.Info($"������ ������� �� ����� {Balance.ServerPort}");
        Log.Info($"����: {DateTime.Now}");
        Log.Info($"�����: {Balance.WorldWidth}x{Balance.WorldHeight}");
        Log.Info($"�������: {DatabaseManager.GetAccountCount()}");
        Log.Info("IP ������ ��� �����������:");
        foreach (var ip in GetLocalIPs())
            Log.Info($"  {ip}");
        Log.Info("�������� �����������...");

        // ��������� �������: ������� �� stdin (���, ������ ������� � �.�.)
        if (args.Any(a => a.Equals("--bot", StringComparison.OrdinalIgnoreCase)))
        {
            StartTestBot();
        }
        _ = Task.Run(() => ServerConsoleLoop());

        int connectionCount = 0;
        while (true)
        {
            TcpClient client = await server.AcceptTcpClientAsync();
            string remoteIp = (client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "unknown";
            Log.Info($"Подключение клиента: {client.Client.RemoteEndPoint}");

            if (_lastConnectTime.TryGetValue(remoteIp, out var lastTime) &&
                DateTime.UtcNow - lastTime < ConnectThrottle)
            {
                Log.Warn($"Отклонено быстрое повторное подключение с {remoteIp}");
                client.Close();
                continue;
            }
            _lastConnectTime[remoteIp] = DateTime.UtcNow;

            // Периодическая очистка старых записей
            if (++connectionCount % 50 == 0)
            {
                var threshold = DateTime.UtcNow - ConnectThrottle;
                foreach (var kv in _lastConnectTime)
                    if (kv.Value < threshold) _lastConnectTime.TryRemove(kv.Key, out _);
            }

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
            connection.Client.ReceiveTimeout = 30000;

            while (!authenticated)
            {
                GameMessage? message = await NetworkHelper.ReceiveAsync<GameMessage>(stream);
                if (message == null)
                {
                    Log.Info($"���������� �������: {connection.Endpoint}");
                    return;
                }

                if (await Services.ClientBuild.HandleUnauthenticatedAsync(connection, message, Services.Hub))
                    continue;

                // ��������������� �������� �� �����������: ReconnectHandler
                // ��������������� ������ � ��� ����������� ��� � ����������.
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
                    Log.Info($"���������� �������: {connection.Endpoint}");
                    break;
                }

                player = await ProcessMessage(connection, message, player ?? connection.Player);
            }
        }
        catch (Exception ex)
        {
            Log.Error($"������: {ex.Message}", ex);
        }
        finally
        {
            if (player != null)
            {
                var tradeSession = Services.Trade.GetSession(player.Id);
                if (tradeSession != null) Services.Trade.CancelSession(tradeSession, "���������� �������");
                player.IsTrading = false;

                bool stillInWorld = Services.World.TryGetPlayerByName(player.Name, out var wp)
                    && ReferenceEquals(wp, player);
                if (stillInWorld)
                {
                    // ����� ����������: ����� ������� � ���� (�������, ������, �������),
                    // ����� �������� ��������������� ������ ��� ������ ���������.
                    // ��������� ������� � ������� sweep'�� ����� ��������� ����.
                    Services.World.MarkPendingReconnect(player);
                    Services.World.RemoveClient(connection);
                    Log.Info($"����� {player.Name} ���������� (���� ��������������� �������)");
                }
                else
                {
                    // ������: LogoutHandler ��� ������ ������ �� ���� � ������ �����.
                    await Services.Party.LeavePartyAsync(player);
                    Services.Instances.RemovePlayer(player);
                    Services.Persistence.EnqueueSave(player);
                    Log.Info($"����� {player.Name} ����� �� ���� (logout)");
                    await Services.Hub.BroadcastMapAsync();
                }
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

            Log.Warn($"����������� ��� ���������: {message.Type}");
        }
        catch (Exception ex)
        {
            Log.Error($"������ ��������� {message.Type}", ex);
        }

        return player;
    }

    /// </summary> <summary>
    /// ������ � ��������� ��������� ���� (�������� �����). ���������� ���
    /// ������ � --bot, ���� �� ������� �������� �bot start�.
    /// </summary>
    private static void StartTestBot()
    {
        lock (_botLock)
        {
            if (_testBot != null)
            {
                Log.Warn("�������� ��� ��� �������.");
                return;
            }

            var bot = new TestBot("127.0.0.1", Balance.ServerPort, "test", "123", "����");
            _testBot = bot;
            _ = Task.Run(() => bot.StartAsync());
            Log.Info("�������� ��� �����������, �����: test / 123");
        }
    }

    /// <summary>
    /// ������� �������: ������ ������� �� stdin (���� Serilog ����� � stdout �
    /// ��� �� �����������) � ��������� ��.
    /// </summary>
    private static async Task ServerConsoleLoop()
    {
        Log.Info("��������� �������: ������� 'help' ��� ������ ������.");

        if (!ConsoleManager.IsInteractiveConsole())
        {
            // ����/����� �������������� (��������, ������ �� �������) � ����������
            // ������� ReadLine, ��� ������� �������.
            ConsoleManager.InputActive = false;
            while (true)
            {
                string? line;
                try { line = await Task.Run(() => Console.ReadLine()); }
                catch { break; }
                if (line == null) break;

                line = line.Trim();
                if (line.Length == 0) continue;

                try { await HandleServerCommand(line); }
                catch (Exception ex) { Log.Error($"[Console] ������: {ex.Message}", ex); }
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
                catch (Exception ex) { Log.Error($"[Console] ������: {ex.Message}", ex); }
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
                Log.Info("������� �������:");
                Log.Info("  players              � ������ ������-�������");
                Log.Info("  bot help             � ������� ��������� ����");
                Log.Info("  bot start / bot stop � ���������/���������� ���� �� ����");
                Log.Info("  stop                 � ���������� ������");
                break;

            case "players":
                var online = Services.World.GetPlayersSnapshot();
                if (online.Count == 0)
                {
                    Log.Info("������: ������");
                }
                else
                {
                    var desc = string.Join(", ", online.Select(p => $"{p.Name} (������� {p.Level})"));
                    Log.Info($"������ ({online.Count}): {desc}");
                }
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
                        Log.Warn("�������� ��� �� �������.");
                        return;
                    }
                    current.Stop();
                    Log.Info("�������� ��� ����������.");
                    return;
                }
                if (sub.Length == 0 || sub.Equals("help", StringComparison.OrdinalIgnoreCase))
                {
                    Log.Info("������� ���� (�������� �����):");
                    Log.Info("  bot start                      � ��������� ���� (������� ������)");
                    Log.Info("  bot stop                       � ���������� ����");
                    Log.Info("  bot say <�����>                � ������� � ��������� ���");
                    Log.Info("  bot whisper <�����> <�����>    � ������ ���������");
                    Log.Info("  bot invite <�����>             � ���������� � ������");
                    Log.Info("  bot leave                      � ����� �� ������");
                    Log.Info("  bot trade <�����>              � ��������� �����");
                    Log.Info("  bot trade_cancel               � �������� �����");
                    Log.Info("  bot mail <�����> <����>        � ��������� ������");
                    Log.Info("    [-- <tid>x<����������> ...]  � � ����������� ���������� (����. -- I0002x2 I0501x1)");
                    Log.Info("  bot move <x> <y>               � �������������");
                    Log.Info("  bot logout                     � ����� �� ����");
                    Log.Info("  (����������� � ������ � ������� ������ ��� ��������� �������������)");
                    return;
                }

                TestBot? bot;
                lock (_botLock) { bot = _testBot; }
                if (bot == null)
                {
                    Log.Warn("�������� ��� �� �������. ������� 'bot start'");
                    return;
                }
                if (!bot.IsConnected)
                {
                    Log.Warn("��� �� ��������� � �������.");
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
                Log.Warn($"����������� �������: {cmd}. ������� 'help'");
                break;
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// ������������� ������: ��������� ���� ������-������� � ���������
    /// ��������� ������� ������ ����� ������� �� ��������.
    /// </summary>
    private static void ShutdownServer()
    {
        try
        {
            Log.Info("��������� �������: ���������� ������ ���� ������-�������...");
            foreach (var conn in Services.World.GetAllConnectionsSnapshot())
            {
                if (conn.Player == null) continue;
                try { Services.Persistence.EnqueueSave(conn.Player); }
                catch (Exception ex) { Log.Warn($"������ ���������� {conn.Player.Name} ��� ���������: {ex.Message}"); }
            }
            _host?.Stop();
            Services.Persistence.Stop();
            Log.Info("������ ����������. �� ��������!");
        }
        catch (Exception ex)
        {
            Log.Error("������ ��� ��������� �������", ex);
        }
        Environment.Exit(0);
    }

    /// <summary>
    /// ��������� Tiled-����� (Content/{fileName}) � ��������� � � ����:
    /// �����, �����������, ����-������������ � ������ ����� ����.
    /// ���� ���� ��� � �� � ����-������������ � ��������� �� �����.
    /// </summary>
    private static List<TiledSpawn>? LoadTiledZone(ZoneManager zones, string fileName, string zoneId)
    {
        string tiledPath = Path.Combine(AppContext.BaseDirectory, "Content", fileName);
        if (!File.Exists(tiledPath))
        {
            Log.Warn($"Tiled-����� �� �������: {tiledPath}");
            return null;
        }

        var tiledMap = TiledMapLoader.Load(tiledPath);

        // ����-����������� ����, ���� � ��� � ��
        if (zoneId != Balance.MainZoneId && zones.GetZone(zoneId) == null)
        {
            zones.RegisterZone(zoneId, tiledMap.Width, tiledMap.Height);
            Log.Info($"���� '{zoneId}' ����-����������������: {tiledMap.Width}x{tiledMap.Height}");
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

        Log.Info($"Tiled-����� {fileName} ��������� � ���� '{zoneId}': {tiledMap.Width}x{tiledMap.Height}, ������: {tileData.Length}, �����������: {obstacles.Count}, ����� ������: {spawns.Count}, ��������: {tiledPortals.Count}");
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
    /// ��������� Tiled-����� ��� standalone GameMap (��� �������� � ����).
    /// </summary>
    private static GameMap? LoadTiledMap(string fileName)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Content", fileName);
        if (!File.Exists(path))
        {
            Log.Warn($"Tiled-����� �� �������: {path}");
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
        Log.Info($"����� {fileName} ���������: {map.Width}x{map.Height}, �����������: {obstacles.Count}");
        return map;
    }
}

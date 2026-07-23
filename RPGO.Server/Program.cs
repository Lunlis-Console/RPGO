using RPGGame.Server.Network;
using RPGGame.Server.MessageHandlers;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace RPGGame.Server;

partial class Program
{
    public static GameServices Services { get; private set; } = null!;
    private static GameServerHost? _host;

    public static List<Player> GetPlayers() => Services.World.GetPlayersSnapshot();
    public static GameWorld GetWorld() => Services.World;

    public static Task RespawnPlayer(Player player)
        => Services.Combat.RespawnPlayer(player);

    public static Task ProcessPendingInteraction(Player player, string interactionType)
        => Services.Interactions.ProcessPendingInteraction(player, interactionType);

    public static int GetAttackSpeed(Player player)
        => Balance.GetAttackSpeedWithWeapon(player.Agility, player.Equipment.GetWeaponSpeedModifier());

    static async Task Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.InputEncoding = System.Text.Encoding.UTF8;

        Log.Init();

        Log.Info("Инициализация базы данных...");
        DatabaseManager.Initialize();
        DatabaseManager.CreateTestAccountIfNeeded();

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

        Log.Info("Загрузка данных (предметы, квесты)...");
        merchant.Initialize();
        quests.Initialize();
        dialogue.LoadAll();
        loot.LoadFromDatabase();

        Log.Info("Загрузка монстров...");
        monsters.Initialize();
        collectibles.Initialize();

        // Создаём сетевой хаб
        var hub = new GameServer(world);

        // Связываем циклические зависимости
        killService.SetHub(hub);
        projectiles.SetHub(hub);
        dialogue.SetHub(hub);
        party.SetHub(hub);
        world.SetDependencies(hub, player => { DatabaseManager.SavePlayerProgress(player); return true; });

        // Создаём контейнер сервисов (временно без Combat/Interactions/Auth — циклическая зависимость)
        Services = new GameServices(world, hub, monsters, loot, corpses, quests, merchant, collectibles,
            trade, dialogue, party, projectiles, killService, pathfinding, debuffs,
            combat: null!, interactions: null!, auth: null!);

        // Создаём сервисы, зависящие от GameServices
        var combat = new CombatService(Services);
        var interactions = new InteractionService(Services);
        var auth = new AuthService(Services);

        // Пересоздаём контейнер с полным набором сервисов
        Services = new GameServices(world, hub, monsters, loot, corpses, quests, merchant, collectibles,
            trade, dialogue, party, projectiles, killService, pathfinding, debuffs,
            combat, interactions, auth);

        // Передаём GameServices в KillService
        killService.SetGameServices(Services);

        MessageHandlerRegistry.RegisterAll(world, hub);

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

                Services.World.RemovePlayer(player);
                Services.World.RemoveClient(connection);
                Log.Info($"Игрок {player.Name} покинул игру");
                await Services.Hub.BroadcastMapAsync();

                DatabaseManager.SavePlayerProgress(player);
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

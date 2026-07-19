using RPGGame.Server.Network;
using RPGGame.Shared.Commands;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;
using RPGGame.Server.MessageHandlers;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;

namespace RPGGame.Server;

partial class Program
{
    // Единый инстанс мира (singleton). Вся игровая логика живёт здесь.
    public static GameWorld World { get; } = new GameWorld(Balance.WorldWidth, Balance.WorldHeight);

    // Сетевой слой сервера (рассылка/отправка сообщений клиентам).
    public static INetworkHub Hub { get; } = new GameServer(World);

    // --- Обратная совместимость: делегируем старые статические поля миру. ---
    // Постепенно менеджеры будут получать World явно; эти обёртки убираются на поздних этапах.

    public static List<Player> GetPlayers() => World.GetPlayersSnapshot();
    public static GameWorld GetWorld() => World;

    static async Task Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.InputEncoding = System.Text.Encoding.UTF8;

        Log.Init();

        Log.Info("Загрузка аккаунтов...");
        DatabaseManager.Initialize();
        DatabaseManager.CreateTestAccountIfNeeded();

        Log.Info("Загрузка контента (магазин, квесты)...");
        MerchantManager.Initialize();
        QuestManager.Initialize();
        LootManager.LoadFromDatabase();

        Log.Info("Создание монстров...");
        MonsterManager.Initialize();
        CollectibleManager.Initialize();

        MessageHandlerRegistry.RegisterAll(World, Hub);

        // Start heartbeat background service
        _ = Task.Run(() => new HeartbeatHandler(World, Hub).StartAsync(CancellationToken.None));

        _ = Task.Run(RunMonsterWanderLoop);
        _ = Task.Run(RunMovePathLoop);
        _ = Task.Run(RunCombatLoop);
        _ = Task.Run(RunMonsterAttackLoop);
        _ = Task.Run(RunRegenLoop);
        _ = Task.Run(RunCorpseCleanupLoop);

        TcpListener server = new TcpListener(IPAddress.Any, Balance.ServerPort);
        server.Start();

        Log.Info($"Сервер запущен на порту {Balance.ServerPort}");
        Log.Info($"Время: {DateTime.Now}");
        Log.Info($"Мир: {Balance.WorldWidth}x{Balance.WorldHeight}");
        Log.Info($"Аккаунтов: {DatabaseManager.GetAccountCount()}");
        Log.Info("IP адреса для подключения:");
        foreach (var ip in GetLocalIPs())
            Log.Info($"  {ip}");
        Log.Info("Ожидание игроков...");

        while (true)
        {
            TcpClient client = await server.AcceptTcpClientAsync();
            Log.Info($"Подключился новый игрок: {client.Client.RemoteEndPoint}");

            ClientConnection connection = new ClientConnection(client);
            World.AddClient(connection);

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

            // Аутентификация
            while (!authenticated)
            {
                GameMessage? message = await NetworkHelper.ReceiveAsync<GameMessage>(stream);
                if (message == null)
                {
                    Log.Info($"Клиент отключился: {connection.Endpoint}");
                    return;
                }

                 authenticated = await HandleAuthMessage(connection, message, Hub);
            }

            // Игровой цикл
            while (true)
            {
                GameMessage? message = await NetworkHelper.ReceiveAsync<GameMessage>(stream);
                if (message == null)
                {
                    Log.Info($"Клиент отключился: {connection.Endpoint}");
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
                var tradeSession = TradeManager.GetSession(player.Id);
                if (tradeSession != null) TradeManager.CancelSession(tradeSession, "игрок отключился");
                player.IsTrading = false;

                World.RemovePlayer(player);
                World.RemoveClient(connection);
                Log.Info($"Игрок {player.Name} покинул мир");
                await Hub.BroadcastMapAsync();

                // Сохраняем прогресс игрока
                DatabaseManager.SavePlayerProgress(player);
            }

            try { connection.Client.Close(); } catch (Exception ex) { Log.Warn($"Close client: {ex.Message}"); }
        }
    }

    public static async Task ReloadContent(ClientConnection? connection = null)
    {
        try
        {
            Log.Info("Перезагрузка контента из БД...");
            MerchantManager.Initialize();
            QuestManager.Initialize();
            LootManager.LoadFromDatabase();
            MonsterManager.Initialize();
            CollectibleManager.Initialize();

            await Hub.BroadcastChatAsync("Система", "Контент перезагружен (предметы, монстры, квесты, мир).");

            if (connection != null)
            {
                await Hub.SendToClient(connection, new GameMessage
                {
                    Type = "chat",
                    Data = new { Name = "Система", Text = "Контент перезагружен из БД." }
                });
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Ошибка перезагрузки: {ex.Message}", ex);
            if (connection != null)
            {
                await Hub.SendToClient(connection, new GameMessage
                {
                    Type = "chat",
                    Data = new { Name = "Система", Text = "Ошибка перезагрузки: " + ex.Message }
                });
            }
        }
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
        catch { /* network interfaces may not be available */ }
        return ips;
    }
}

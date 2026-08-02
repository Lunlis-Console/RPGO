using RPGGame.Shared.Models;
using RPGGame.Shared.Network;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text.Json;
namespace RPGGame.Server;

/// <summary>
/// Головной тестовый бот: подключается к серверу как обычный сетевой клиент
/// (аккаунт test/123) и выполняет игровые команды, введённые в консоль сервера.
/// Позволяет тестировать взаимодействие между игроками (партия, обмен, почта, чат)
/// без запуска нескольких клиентов.
/// </summary>
public class TestBot : IDisposable
{
    private readonly string _host;
    private readonly int _port;
    private readonly string _login;
    private readonly string _password;
    private readonly ConcurrentQueue<string> _commands = new();
    private readonly CancellationTokenSource _cts = new();
    private TcpClient? _client;
    private Stream? _stream;
    private long _pingSeq;
    private string _lastPartyInviter = "";
    private string _lastTradeInviter = "";

    private static readonly HashSet<string> _noisyTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "map_update", "status_response", "pong", "player_facing", "player_move",
        "player_hp", "hotbar_response", "inventory_response", "quest_log",
        "skills_response", "equipment_response", "spell_response", "quest_update"
    };

    public bool IsConnected => _client?.Connected == true;
    public string Name { get; }

    public TestBot(string host, int port, string login, string password, string playerName)
    {
        _host = host;
        _port = port;
        _login = login;
        _password = password;
        Name = playerName;
    }

    public void EnqueueCommand(string command) => _commands.Enqueue(command);

    public async Task StartAsync()
    {
        try
        {
            _client = new TcpClient();
            await _client.ConnectAsync(_host, _port);
            _stream = _client.GetStream();
            Log.Info($"[Бот {Name}] Подключён к {_host}:{_port}");

            await SendAsync(new GameMessage
            {
                Type = "login_auth",
                Data = new { Login = _login, Password = _password }
            });

            _ = Task.Run(() => ReceiveLoopAsync(_cts.Token));
            _ = Task.Run(() => HeartbeatLoopAsync(_cts.Token));
            _ = Task.Run(() => CommandLoopAsync(_cts.Token));
        }
        catch (Exception ex)
        {
            Log.Error($"[Бот {Name}] Ошибка подключения", ex);
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _client?.Connected == true)
        {
            GameMessage? msg;
            var stream = _stream;
            try
            {
                msg = await NetworkHelper.ReceiveAsync<GameMessage>(stream!, token);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Log.Error($"[Бот {Name}] Ошибка чтения", ex);
                break;
            }
            if (msg == null) break;
            HandleMessage(msg);
        }
        Log.Info($"[Бот {Name}] Соединение закрыто");
    }

    private void HandleMessage(GameMessage msg)
    {
        switch (msg.Type)
        {
            case "welcome":
                Log.Info($"[Бот {Name}] Вошёл в мир");
                break;
            case "party_invite_received":
                _lastPartyInviter = GetProp(msg.Data, "InviterName") ?? "";
                Log.Info($"[Бот {Name}] Приглашение в группу от: {_lastPartyInviter} (bot accept — принять, bot decline — отклонить)");
                break;
            case "trade_request_received":
                _lastTradeInviter = GetProp(msg.Data, "InviterName") ?? "";
                Log.Info($"[Бот {Name}] Запрос обмена от: {_lastTradeInviter} (bot trade_accept / bot trade_decline)");
                break;
            case "mail_unread":
                Log.Info($"[Бот {Name}] Непрочитанных писем: {GetProp(msg.Data, "Count") ?? "?"}");
                break;
            default:
                // Пропускаем высокочастотные служебные сообщения, иначе бот
                // спамит консоль сервера (map_update/status_response приходят
                // от каждого игрока каждую секунду).
                if (!_noisyTypes.Contains(msg.Type))
                    Log.Debug($"[Бот {Name}] ← {msg.Type}");
                break;
        }
    }

    private async Task HeartbeatLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _client?.Connected == true)
        {
            try { await Task.Delay(5000, token); }
            catch (OperationCanceledException) { break; }

            try
            {
                var seq = Interlocked.Increment(ref _pingSeq);
                await SendAsync(new GameMessage
                {
                    Type = "ping",
                    Data = new PingMessage(seq, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
                });
            }
            catch (Exception ex) { Log.Error($"[Бот {Name}] Ошибка ping", ex); break; }
        }
    }

    private async Task CommandLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _client?.Connected == true)
        {
            if (_commands.TryDequeue(out var cmd))
            {
                try { await ExecuteCommandAsync(cmd); }
                catch (Exception ex) { Log.Error($"[Бот {Name}] Ошибка команды '{cmd}'", ex); }
            }
            else
            {
                try { await Task.Delay(100, token); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private async Task ExecuteCommandAsync(string cmd)
    {
        var parts = cmd.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;
        string action = parts[0].ToLowerInvariant();

        switch (action)
        {
            case "say":
                string text = parts.Length > 1 ? cmd.Substring(cmd.IndexOf(' ') + 1).Trim() : "";
                if (text.Length == 0) { Log.Warn($"[Бот {Name}] bot say <текст>"); break; }
                await SendAsync(new GameMessage { Type = "say", Data = text });
                Log.Info($"[Бот {Name}] Сказал: {text}");
                break;

            case "whisper":
                if (parts.Length < 3) { Log.Warn($"[Бот {Name}] bot whisper <игрок> <текст>"); break; }
                string wMsg = cmd.Substring(cmd.IndexOf(' ', cmd.IndexOf(' ') + 1) + 1).Trim();
                await SendAsync(new GameMessage { Type = "say", Data = $"/w {parts[1]} {wMsg}" });
                Log.Info($"[Бот {Name}] Шепнул {parts[1]}: {wMsg}");
                break;

            case "invite":
                if (parts.Length < 2) { Log.Warn($"[Бот {Name}] bot invite <игрок>"); break; }
                await SendAsync(new GameMessage { Type = "party_invite", Data = new { TargetName = parts[1] } });
                Log.Info($"[Бот {Name}] Пригласил в группу: {parts[1]}");
                break;

            case "accept":
                string inviter = parts.Length > 1 ? parts[1] : _lastPartyInviter;
                if (inviter.Length == 0) { Log.Warn($"[Бот {Name}] Нет активного приглашения"); break; }
                await SendAsync(new GameMessage { Type = "party_accept", Data = new { InviterName = inviter } });
                Log.Info($"[Бот {Name}] Принял приглашение от: {inviter}");
                break;

            case "decline":
                inviter = parts.Length > 1 ? parts[1] : _lastPartyInviter;
                if (inviter.Length == 0) { Log.Warn($"[Бот {Name}] Нет активного приглашения"); break; }
                await SendAsync(new GameMessage { Type = "party_decline", Data = new { InviterName = inviter } });
                Log.Info($"[Бот {Name}] Отклонил приглашение от: {inviter}");
                break;

            case "trade":
                if (parts.Length < 2) { Log.Warn($"[Бот {Name}] bot trade <игрок>"); break; }
                await SendAsync(new GameMessage { Type = "trade_request", Data = new { TargetName = parts[1] } });
                Log.Info($"[Бот {Name}] Запросил обмен с: {parts[1]}");
                break;

            case "trade_accept":
                string trader = parts.Length > 1 ? parts[1] : _lastTradeInviter;
                if (trader.Length == 0) { Log.Warn($"[Бот {Name}] Нет активного запроса обмена"); break; }
                await SendAsync(new GameMessage { Type = "trade_accept", Data = new { InviterName = trader } });
                Log.Info($"[Бот {Name}] Принял обмен с: {trader}");
                break;

            case "trade_decline":
                trader = parts.Length > 1 ? parts[1] : _lastTradeInviter;
                if (trader.Length == 0) { Log.Warn($"[Бот {Name}] Нет активного запроса обмена"); break; }
                await SendAsync(new GameMessage { Type = "trade_decline", Data = new { InviterName = trader } });
                Log.Info($"[Бот {Name}] Отклонил обмен с: {trader}");
                break;

            case "mail":
                if (parts.Length < 3) { Log.Warn($"[Бот {Name}] bot mail <получатель> <тема>"); break; }
                int recipientEnd = cmd.IndexOf(' ', cmd.IndexOf(' ') + 1);
                string subject = cmd.Substring(recipientEnd + 1).Trim();
                await SendAsync(new GameMessage
                {
                    Type = "mail",
                    Data = new { Action = "send", RecipientName = parts[1], Subject = subject, Body = "", GoldAmount = 0, ItemId = "", ItemQuantity = 0 }
                });
                Log.Info($"[Бот {Name}] Отправил письмо: {parts[1]}, тема «{subject}»");
                break;

            case "move":
                if (parts.Length < 3 || !int.TryParse(parts[1], out int mx) || !int.TryParse(parts[2], out int my))
                { Log.Warn($"[Бот {Name}] bot move <x> <y>"); break; }
                await SendAsync(new GameMessage { Type = "move_to", Data = new { X = mx, Y = my } });
                Log.Info($"[Бот {Name}] Идёт в ({mx}, {my})");
                break;

            case "logout":
                await SendAsync(new GameMessage { Type = "logout" });
                Log.Info($"[Бот {Name}] Выходит из игры");
                break;

            default:
                Log.Warn($"[Бот {Name}] Неизвестная команда: {action}. Введите 'bot help'");
                break;
        }
    }

    private static string? GetProp(object? data, string prop)
    {
        if (data is JsonElement el && el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v))
            return v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString();
        return null;
    }

    private async Task SendAsync(GameMessage msg)
    {
        if (_stream == null) return;
        await NetworkHelper.SendAsync(_stream, msg);
    }

    public void Stop()
    {
        _cts.Cancel();
        try { _client?.Close(); } catch { }
    }

    public void Dispose() => Stop();
}

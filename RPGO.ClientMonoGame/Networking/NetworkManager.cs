using RPGGame.Shared.Models;
using RPGGame.Shared.Network;
using System.Net.Sockets;

namespace RPGGame.ClientMonoGame.Networking;

public class NetworkManager
{
    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private Task? _heartbeatTask;
    private CancellationTokenSource? _reconnectCts;
    // Single-flight: 1, если попытка переподключения уже выполняется.
    private int _reconnectInProgress;

    private volatile bool _isConnected = false;
    private int _missedPongs = 0;
    private long _lastPingSeq = 0;
    private DateTime _lastPongTime = DateTime.UtcNow;

    private string _serverIp = "127.0.0.1";
    private string? _sessionToken;
    private Guid _playerId;

    public event Action? Connected;
    public event Action? Disconnected;
    public event Action<string>? ConnectionLost;
    public event Action<string>? SystemMessage;
    public event Action<GameMessage>? MessageReceived;
    public event Action<PlayerState>? ReconnectStateReceived;
    public event Action? ReconnectFailed;

    /// <summary>Максимальное время попыток переподключения (после — возврат к экрану входа).
    /// 30 секунд: даёт время перезапустить сервер и вернуться в игру.</summary>
    public const int ReconnectTimeoutSeconds = 30;

    private DateTime _reconnectDeadline;

    public bool IsConnected => _isConnected;

    /// <summary>Есть ли активная сессия (токен переподключения), т.е. ожидается ли авто-reconnect.</summary>
    public bool HasSession => !string.IsNullOrEmpty(_sessionToken);

    public async Task<bool> ConnectAsync(string ip, int port)
    {
        _serverIp = ip;
        return await ConnectInternalAsync(ip, port);
    }

    private async Task<bool> ConnectInternalAsync(string ip, int port)
    {
        try
        {
            // Закрываем предыдущее соединение/потоки перед новой попыткой,
            // чтобы не копить осиротевшие сокеты и receive-loop'ы при ретраях.
            try { _cts?.Cancel(); } catch { }
            try { _client?.Close(); } catch { }

            _client = new TcpClient { NoDelay = true };
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await _client.ConnectAsync(ip, port, cts.Token);
            _stream = _client.GetStream();

            _isConnected = true;
            _missedPongs = 0;
            _lastPongTime = DateTime.UtcNow;

            _cts = new CancellationTokenSource();
            _receiveTask = Task.Run(() => ReceiveLoopAsync(_cts.Token, _stream));
            _heartbeatTask = Task.Run(() => HeartbeatLoopAsync(_cts.Token));

            Logger.Info($"Connected to {ip}:{port}");
            Connected?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"Connect failed to {ip}:{port}", ex);
            SystemMessage?.Invoke($"Ошибка подключения: {ex.Message}");
            return false;
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken token, NetworkStream stream)
    {
        try
        {
            while (!token.IsCancellationRequested && stream != null)
            {
                var message = await NetworkHelper.ReceiveAsync<GameMessage>(stream, token);
                if (message == null) break;

                Logger.Debug($"<< recv {message.Type}");

                if (HandleSystemMessage(message))
                    continue;

                MessageReceived?.Invoke(message);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Logger.Error("ReceiveLoop error", ex);
            if (!token.IsCancellationRequested)
                SystemMessage?.Invoke($"Ошибка сети: {ex.Message}");
        }
        finally
        {
            Logger.Warn("ReceiveLoop ended, handling disconnect");
            try { HandleDisconnectAsync("Соединение разорвано", token, stream).Wait(); }
            catch { }
        }
    }

    private bool HandleSystemMessage(GameMessage message)
    {
        switch (message.Type)
        {
            case "pong":
            case "ping":
                _missedPongs = 0;
                _lastPongTime = DateTime.UtcNow;
                return true;

            case "kick":
                SystemMessage?.Invoke("Вы были отключены");
                return true;

            case "reconnect_ok":
                var response = message.Deserialize<ReconnectResponse>();
                if (response?.Success == true && response.Player != null)
                {
                    // Попытка переподключения завершена успешно: глобальный дедлайн
                    // сбрасываем, чтобы следующий обрыв начал отсчёт заново.
                    _reconnectDeadline = default;
                    // После перезапуска сервера игрок был пересоздан — у него новый Id.
                    if (response.PlayerId != null)
                    {
                        _playerId = response.PlayerId.Value;
                        if (GameMain.Instance?.Client != null)
                            GameMain.Instance.Client.PlayerId = response.PlayerId.Value;
                    }
                    // Сервер выдаёт свежий одноразовый токен для следующего реконнекта
                    if (!string.IsNullOrEmpty(response.Token))
                        SetSession(response.Token, _playerId);
                    else
                        _sessionToken = null;
                    ReconnectStateReceived?.Invoke(response.Player);
                }
                return true;

            case "reconnect_fail":
                var fail = message.Deserialize<ReconnectResponse>();
                SystemMessage?.Invoke($"Реконнект не удался: {fail?.Reason ?? "unknown"}");
                // Терминальная ошибка: сервер не принял старую сессию (токен истёк,
                // игрок удалён из мира или неверный запрос). Повторные попытки с тем же
                // токеном бессмысленны — сразу возвращаем игрока к экрану входа.
                ReconnectFailed?.Invoke();
                return true;
        }
        return false;
    }

    private async Task HeartbeatLoopAsync(CancellationToken token)
    {
        var stream = _stream;
        while (!token.IsCancellationRequested)
        {
            await Task.Delay(5000, token);
            if (token.IsCancellationRequested) break;

            try
            {
                _lastPingSeq = Interlocked.Increment(ref _lastPingSeq);
                var ping = new PingMessage(_lastPingSeq, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                await SendAsync(new GameMessage { Type = "ping", Data = ping });

                await Task.Delay(5000, token);
                if (DateTime.UtcNow - _lastPongTime > TimeSpan.FromSeconds(15))
                {
                    _missedPongs++;
                    if (_missedPongs >= 3)
                    {
                        SystemMessage?.Invoke("Сервер не отвечает, переподключение...");
                        await HandleDisconnectAsync("Таймаут сервера", token, stream);
                        break;
                    }
                }
            }
            catch (Exception hbEx)
            {
                Logger.Error("[hb] ping send error", hbEx);
                _missedPongs++;
                if (_missedPongs >= 3)
                {
                    await HandleDisconnectAsync("Ошибка отправки ping", token, stream);
                    break;
                }
            }
        }
    }

    private async Task HandleDisconnectAsync(string reason, CancellationToken token, NetworkStream? stream)
    {
        // Старый receive/heartbeat-loop после успешного реконнекта не должен «убивать»
        // новое соединение: если это уже не текущее соединение — выходим.
        if (!ReferenceEquals(stream, _stream)) return;
        if (token != _cts?.Token) return;

        if (!_isConnected) return;
        _isConnected = false;
        Logger.Warn($"Disconnected: {reason}");

        ConnectionLost?.Invoke(reason);
        // Disconnected (без причины) уведомляем только в ручном Disconnect():
        // здесь достаточно ConnectionLost с конкретной причиной.

        if (!string.IsNullOrEmpty(_sessionToken))
            await StartReconnectAsync();
    }

    private async Task StartReconnectAsync()
    {
        if (string.IsNullOrEmpty(_sessionToken))
            return;

        // Single-flight: если попытка переподключения уже идёт (StartReconnectAsync
        // вызывается и из receive-лупа, и из heartbeat, и из обработчиков сообщений),
        // не запускаем вторую параллельно.
        if (Interlocked.Exchange(ref _reconnectInProgress, 1) != 0)
            return;

        try
        {
            // Глобальный дедлайн всей попытки переподключения: не сбрасывается при
            // повторных вызовах (reconnect_fail → новый цикл), чтобы обрыв не мог
            // удерживать игрока в оверлее переподключения бесконечно.
            if (_reconnectDeadline == default)
                _reconnectDeadline = DateTime.UtcNow.AddSeconds(ReconnectTimeoutSeconds);

            using var cts = new CancellationTokenSource();
            _reconnectCts = cts;
            int attempt = 0;
            int delayMs = 1000;

            while (attempt < 20 && !cts.IsCancellationRequested &&
                   DateTime.UtcNow < _reconnectDeadline)
            {
                attempt++;
                SystemMessage?.Invoke($"Попытка переподключения {attempt}...");

                if (await ConnectInternalAsync(_serverIp, 7777))
                {
                    try
                    {
                        var req = new ReconnectRequest(_playerId, _sessionToken ?? "", _lastPingSeq);
                        await SendAsync(new GameMessage { Type = "reconnect", Data = req });
                        return;
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("[reconnect] send failed", ex);
                        // Соединение поднялось, но запрос не ушёл — закрываем
                        // и пробуем следующую попытку.
                        try { _cts?.Cancel(); } catch { }
                        try { _client?.Close(); } catch { }
                        _isConnected = false;
                    }
                }

                try { await Task.Delay(delayMs, cts.Token); }
                catch (OperationCanceledException) { break; }
                delayMs = Math.Min(delayMs * 2, 3000);
            }

            if (!cts.IsCancellationRequested)
            {
                _reconnectDeadline = default;
                SystemMessage?.Invoke("Не удалось переподключиться.");
                ReconnectFailed?.Invoke();
            }
        }
        finally
        {
            _reconnectCts = null;
            Interlocked.Exchange(ref _reconnectInProgress, 0);
        }
    }

    /// <summary>
    /// Отменяет фоновые попытки переподключения, закрывает соединение и сбрасывает
    /// сессию (вызывается при возврате к экрану входа после неудачного reconnect).
    /// </summary>
    public void StopReconnect()
    {
        try { _reconnectCts?.Cancel(); } catch { }
        _sessionToken = null;
        _reconnectDeadline = default;
        _isConnected = false;
        try { _cts?.Cancel(); } catch { }
        try { _client?.Close(); } catch { }
    }

    public async Task SendAsync(GameMessage message)
    {
        Logger.Debug($">> send {message.Type}");
        if (_stream == null) throw new InvalidOperationException("Not connected");
        await NetworkHelper.SendAsync(_stream, message);
    }

    public void SetSession(string token, Guid playerId)
    {
        _sessionToken = token;
        _playerId = playerId;
    }

    public void Disconnect()
    {
        // Сбрасываем токен, чтобы не запускался авто-reconnect при возврате в меню
        _sessionToken = null;
        _isConnected = false;
        try { _cts?.Cancel(); } catch { }
        try { _client?.Close(); } catch { }
        Disconnected?.Invoke();
    }
}

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

    public bool IsConnected => _isConnected;

    public async Task<bool> ConnectAsync(string ip, int port)
    {
        _serverIp = ip;
        return await ConnectInternalAsync(ip, port);
    }

    private async Task<bool> ConnectInternalAsync(string ip, int port)
    {
        try
        {
            _client = new TcpClient { NoDelay = true };
            await _client.ConnectAsync(ip, port);
            _stream = _client.GetStream();

            _isConnected = true;
            _missedPongs = 0;
            _lastPongTime = DateTime.UtcNow;

            _cts = new CancellationTokenSource();
            _receiveTask = Task.Run(() => ReceiveLoopAsync(_cts.Token));
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

    private async Task ReceiveLoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested && _stream != null)
            {
                var message = await NetworkHelper.ReceiveAsync<GameMessage>(_stream, token);
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
            HandleDisconnectAsync("Соединение разорвано").Wait();
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
                    _sessionToken = null;
                    ReconnectStateReceived?.Invoke(response.Player);
                }
                return true;

            case "reconnect_fail":
                var fail = message.Deserialize<ReconnectResponse>();
                SystemMessage?.Invoke($"Реконнект не удался: {fail?.Reason ?? "unknown"}");
                _ = StartReconnectAsync();
                return true;
        }
        return false;
    }

    private async Task HeartbeatLoopAsync(CancellationToken token)
    {
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
                        await HandleDisconnectAsync("Таймаут сервера");
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
                    await HandleDisconnectAsync("Ошибка отправки ping");
                    break;
                }
            }
        }
    }

    private async Task HandleDisconnectAsync(string reason)
    {
        if (!_isConnected) return;
        _isConnected = false;
        Logger.Warn($"Disconnected: {reason}");

        ConnectionLost?.Invoke(reason);
        Disconnected?.Invoke();

        if (!string.IsNullOrEmpty(_sessionToken))
            await StartReconnectAsync();
    }

    private async Task StartReconnectAsync()
    {
        int attempt = 0;
        int delayMs = 1000;

        while (attempt < 10)
        {
            attempt++;
            SystemMessage?.Invoke($"Попытка переподключения {attempt}/10...");

            if (await ConnectInternalAsync(_serverIp, 7777))
            {
                var req = new ReconnectRequest(_playerId, _sessionToken ?? "", _lastPingSeq);
                await SendAsync(new GameMessage { Type = "reconnect", Data = req });
                return;
            }

            await Task.Delay(delayMs);
            delayMs = Math.Min(delayMs * 2, 30000);
        }

        SystemMessage?.Invoke("Не удалось переподключиться.");
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

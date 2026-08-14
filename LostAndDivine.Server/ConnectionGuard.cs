using System.Collections.Concurrent;

namespace LostAndDivine.Server;

/// <summary>
/// Защита от сканеров/мусорных подключений: лимит частоты подключений с одного IP
/// и временный автобан IP с повторными неудачными (мусорными) подключениями.
/// Настоящая авторизация игроков остаётся обязательной — сюда попадают только
/// TCP-подключения, не прошедшие ни одного валидного сообщения.
/// </summary>
public class ConnectionGuard
{
    private sealed class IpState
    {
        public readonly List<DateTime> RecentConnections = new();
        public int Strikes;
        public DateTime BannedUntil = DateTime.MinValue;
        public DateTime LastAccess = DateTime.UtcNow;
    }

    private readonly ConcurrentDictionary<string, IpState> _states = new(StringComparer.Ordinal);

    private readonly int _maxConnectionsPerWindow;
    private readonly TimeSpan _window;
    private readonly int _maxStrikes;
    private readonly TimeSpan _banDuration;
    private DateTime _lastSweep = DateTime.UtcNow;

    public ConnectionGuard(int maxConnectionsPerWindow = 10, int windowSeconds = 60,
        int maxStrikes = 4, int banMinutes = 15)
    {
        _maxConnectionsPerWindow = maxConnectionsPerWindow;
        _window = TimeSpan.FromSeconds(windowSeconds);
        _maxStrikes = maxStrikes;
        _banDuration = TimeSpan.FromMinutes(banMinutes);
    }

    /// <summary>Из "::ffff:192.168.1.5:7777" -> "192.168.1.5".</summary>
    public static string NormalizeIp(string endpoint)
    {
        if (string.IsNullOrEmpty(endpoint)) return endpoint;

        int colon = endpoint.LastIndexOf(':');
        if (colon > 0) endpoint = endpoint.Substring(0, colon);

        endpoint = endpoint.TrimStart('[').TrimEnd(']');
        if (endpoint.StartsWith("::ffff:", StringComparison.OrdinalIgnoreCase))
            endpoint = endpoint.Substring(7);
        return endpoint;
    }

    /// <summary>
    /// Разрешено ли принять новое подключение с этого IP. Лимит частоты
    /// подключений; превышение считается штрафом и может привести к бану.
    /// </summary>
    public bool Allow(string ip)
    {
        Sweep();
        var state = _states.GetOrAdd(ip, _ => new IpState());

        lock (state)
        {
            state.LastAccess = DateTime.UtcNow;
            if (state.BannedUntil > DateTime.UtcNow)
                return false;

            state.RecentConnections.RemoveAll(t => t < DateTime.UtcNow - _window);
            state.RecentConnections.Add(DateTime.UtcNow);

            if (state.RecentConnections.Count > _maxConnectionsPerWindow)
            {
                Strike(ip, state);
                return false;
            }
            return true;
        }
    }

    /// <summary>Подключение прошло успешно (клиент прислал валидное сообщение/авторизовался) — сброс штрафов.</summary>
    public void RecordSuccess(string ip)
    {
        if (string.IsNullOrEmpty(ip)) return;
        if (_states.TryGetValue(ip, out var state))
        {
            lock (state)
            {
                state.Strikes = 0;
                state.RecentConnections.Clear();
            }
        }
    }

    /// <summary>Мусорное подключение: ни одного валидного сообщения до отключения.</summary>
    public void RecordFailure(string ip)
    {
        if (string.IsNullOrEmpty(ip)) return;
        var state = _states.GetOrAdd(ip, _ => new IpState());
        lock (state)
        {
            Strike(ip, state);
        }
    }

    private void Strike(string ip, IpState state)
    {
        state.Strikes++;
        if (state.Strikes >= _maxStrikes)
        {
            state.BannedUntil = DateTime.UtcNow + _banDuration;
            state.Strikes = 0;
            state.RecentConnections.Clear();
            Log.Info($"IP {ip} временно забанен ({_maxStrikes} мусорных подключений, {_banDuration.TotalMinutes:N0} мин)");
        }
    }

    private void Sweep()
    {
        DateTime now = DateTime.UtcNow;
        if (now - _lastSweep < TimeSpan.FromSeconds(1)) return;
        _lastSweep = now;

        DateTime cutoff = now - (_banDuration + _window);
        foreach (var kv in _states)
        {
            if (kv.Value.LastAccess < cutoff)
                _states.TryRemove(kv.Key, out _);
        }
    }
}
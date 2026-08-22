using LostAndDivine.Shared.Models;
using System.Collections.Concurrent;
using System.Net.Sockets;

namespace LostAndDivine.Server;

public class ClientConnection : IDisposable
{
    public TcpClient Client { get; }
    public string Endpoint { get; }
    public Player? Player { get; set; }
    public string? AuthenticatedLogin { get; set; }
    public bool IsAdmin { get; set; }
    public SemaphoreSlim WriteLock { get; } = new(1, 1);
    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { WriteLock.Dispose(); } catch { }
        try { Client.Dispose(); } catch { }
    }

    // Heartbeat tracking
    public DateTime LastPongReceived { get; set; } = DateTime.UtcNow;
    public long LastPingSeq { get; set; } = 0;
    public bool IsReconnecting { get; set; } = false;
    public string? SessionToken { get; set; }
    public bool IsMuted { get; set; }

    /// <summary>
    /// Клиент получил приветствие (welcome/reconnect_ok). До этого момента
    /// BroadcastMapAsync не должен слать map_update: иначе тайлы могут дойти
    /// раньше создания GameScreen на клиенте и потеряться (баг «трава»).
    /// </summary>
    public bool WelcomeSent { get; set; }

    // Тайлы отправляются один раз на зону в рамках соединения (а не один флаг на все зоны).
    // Это исключает гонку: при смене зоны клиент всегда получит тайлы новой зоны,
    // даже если конкурентный BroadcastMapAsync успел отправить map_update без тайлов.
    private readonly ConcurrentDictionary<string, byte> _tilesSentZones = new();

    public bool HasTilesSent(string zoneId) => _tilesSentZones.ContainsKey(zoneId);
    public void MarkTilesSent(string zoneId) => _tilesSentZones[zoneId] = 0;

    // Секторы открытого мира (main), уже отправленные клиенту. Сектор шлётся один
    // раз за соединение (100x100 тайлов ~10КБ; карта 3000x1700 целиком не передаётся).
    private readonly ConcurrentDictionary<(int Col, int Row), byte> _sectorsSent = new();

    public bool HasSectorSent(int col, int row) => _sectorsSent.ContainsKey((col, row));
    public void MarkSectorSent(int col, int row) => _sectorsSent[(col, row)] = 0;
    public void ResetSectorsSent() => _sectorsSent.Clear();

    // Полный сброс кэша «тайлы отправлены» при смене зоны. На клиенте хранится только
    // ОДИН буфер тайлов (под текущую зону). Если не сбросить флаги, при возврате в уже
    // виденную зону (напр. из Арены в мир) server посчитает, что клиент тайлы уже имеет,
    // и не пришлёт их — клиент останется со старым буфером другого размера → «куски»/белые
    // тайлы. Сброс гарантирует, что после перехода всегда придёт полный набор тайлов.
    public void ResetTilesSent()
    {
        _tilesSentZones.Clear();
        ResetSectorsSent();
    }

    public ClientConnection(TcpClient client)
    {
        Client = client;
        Endpoint = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
        client.NoDelay = true;
        try
        {
            client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
        }
        catch { /* KeepAlive option not critical */ }
    }
}

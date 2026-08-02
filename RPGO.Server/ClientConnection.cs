using RPGGame.Shared.Models;
using System.Collections.Concurrent;
using System.Net.Sockets;

namespace RPGGame.Server;

public class ClientConnection
{
    public TcpClient Client { get; }
    public string Endpoint { get; }
    public Player? Player { get; set; }
    public SemaphoreSlim WriteLock { get; } = new(1, 1);

    // Heartbeat tracking
    public DateTime LastPongReceived { get; set; } = DateTime.UtcNow;
    public long LastPingSeq { get; set; } = 0;
    public bool IsReconnecting { get; set; } = false;
    public string? SessionToken { get; set; }

    // Тайлы отправляются один раз на зону в рамках соединения (а не один флаг на все зоны).
    // Это исключает гонку: при смене зоны клиент всегда получит тайлы новой зоны,
    // даже если конкурентный BroadcastMapAsync успел отправить map_update без тайлов.
    private readonly ConcurrentDictionary<string, byte> _tilesSentZones = new();

    public bool HasTilesSent(string zoneId) => _tilesSentZones.ContainsKey(zoneId);
    public void MarkTilesSent(string zoneId) => _tilesSentZones[zoneId] = 0;

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

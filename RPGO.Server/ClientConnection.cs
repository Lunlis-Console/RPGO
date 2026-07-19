using RPGGame.Shared.Models;
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

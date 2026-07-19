using Microsoft.Extensions.Hosting;
using RPGGame.Server.Network;
using RPGGame.Server.Services;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;
using System.Threading;

namespace RPGGame.Server.MessageHandlers;

/// <summary>
/// Background service that checks connection health via heartbeat.
/// Runs every 5 seconds, disconnects clients that missed 3 pings (15 sec timeout).
/// </summary>
public class HeartbeatHandler : BackgroundService
{
    private readonly GameWorld _world;
    private readonly INetworkHub _hub;
    private readonly TimeSpan _checkInterval = TimeSpan.FromSeconds(5);
    private readonly int _maxMissedPings = 3; // 3 * 5s = 15s timeout

    public HeartbeatHandler(GameWorld world, INetworkHub hub)
    {
        _world = world;
        _hub = hub;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                CheckConnections();
                SessionManager.Cleanup();

                _tick++;
                if (_tick % 12 == 0) // каждые ~60s
                    AutoSaveAll();
            }
            catch (Exception ex)
            {
                Log.Error("[Heartbeat] Error", ex);
            }

            await Task.Delay(_checkInterval, stoppingToken);
        }
    }

    private int _tick;

    private void AutoSaveAll()
    {
        try
        {
            foreach (var conn in _world.GetAllConnectionsSnapshot())
            {
                if (conn.Player == null) continue;
                try { DatabaseManager.SavePlayerProgress(conn.Player); }
                catch (Exception ex) { Log.Debug($"[AutoSave] Failed for {conn.Player.Name}: {ex.Message}"); }
            }
        }
        catch (Exception ex)
        {
            Log.Error("[AutoSave] Error", ex);
        }
    }

    private void CheckConnections()
    {
        var now = DateTime.UtcNow;
        var toRemove = new List<ClientConnection>();

        foreach (var conn in _world.GetAllConnectionsSnapshot())
        {
            if (conn.Player == null) continue; // Not authenticated yet
            if (conn.IsReconnecting) continue; // In reconnect process

            var timeSincePong = now - conn.LastPongReceived;
            var missedIntervals = (int)(timeSincePong.TotalSeconds / 5);

            if (missedIntervals >= _maxMissedPings)
            {
                Log.Warn($"[Heartbeat] Disconnecting {conn.Player.Name} (missed {missedIntervals} pings, {timeSincePong.TotalSeconds:F1}s)");
                toRemove.Add(conn);
            }
            else if (missedIntervals > 0)
            {
                // Send a ping to keep alive
                _ = SendPing(conn);
            }
        }

        foreach (var conn in toRemove)
        {
            _world.RemoveClient(conn);
            try { conn.Client.Close(); } catch { /* already disconnecting */ }
        }
    }

    private async Task SendPing(ClientConnection conn)
    {
        try
        {
            var seq = Interlocked.Increment(ref _seqCounter);
            conn.LastPingSeq = seq;
            await _hub.SendToClient(conn, new GameMessage
            {
                Type = "ping",
                Data = new PingMessage(seq, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
            });
        }
        catch (Exception ex) { Log.Debug($"[Heartbeat] Ping failed: {ex.Message}"); }
    }

    private long _seqCounter = 0;
}

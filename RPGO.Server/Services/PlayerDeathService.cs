using RPGGame.Server.Network;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;

namespace RPGGame.Server;

public class PlayerDeathService
{
    private readonly Lazy<GameServices> _svcLazy;
    private GameServices _svc => _svcLazy.Value;

    public PlayerDeathService(Lazy<GameServices> svc)
    {
        _svcLazy = svc;
    }

    public async Task HandlePlayerDeath(Player pl, ClientConnection client)
    {
        int lostGold = Balance.ComputeDeathGoldLoss(pl.Gold);
        pl.Gold -= lostGold;
        pl.IsDead = true;
        pl.DeathTime = DateTime.UtcNow;
        Log.Info($"{pl.Name} погиб! Потеряно {lostGold} золота. Таймер 5с.");
        await _svc.Hub.SendToClient(client, GameMessage.ResetCombat());
        await _svc.Hub.SendToClient(client, GameMessage.PlayerDeath(lostGold));
        await _svc.ChatTo(client, ChatChannel.System, "Система", $"Вы погибли! Потеряно {lostGold} золота. Возрождение через 5 сек...");
        await _svc.Party.SendUpdateForAsync(pl);
    }

    public async Task RespawnPlayer(Player pl, int? forceX = null, int? forceY = null)
    {
        pl.IsDead = false;
        pl.Health = Balance.RespawnHealth(pl.MaxHealth);

        if (forceX.HasValue && forceY.HasValue)
        {
            pl.X = forceX.Value;
            pl.Y = forceY.Value;
        }
        else
        {
            var zone = _svc.Zones.GetZone(pl.CurrentZoneId);

            int baseX, baseY, mapW, mapH;
            if (zone != null && zone.PvpEnabled)
            {
                var safeZone = _svc.Zones.Zones.Values
                    .Where(z => !z.PvpEnabled && (z.SpawnX > 0 || z.SpawnY > 0))
                    .OrderBy(z => Math.Abs(z.SpawnX - _svc.Merchant.MerchantX) + Math.Abs(z.SpawnY - _svc.Merchant.MerchantY))
                    .FirstOrDefault();

                if (safeZone != null)
                {
                    baseX = safeZone.SpawnX;
                    baseY = safeZone.SpawnY;
                    mapW = safeZone.Width;
                    mapH = safeZone.Height;
                    pl.CurrentZoneId = safeZone.Id;
                }
                else
                {
                    baseX = _svc.Merchant.MerchantX;
                    baseY = _svc.Merchant.MerchantY;
                    mapW = zone.Width;
                    mapH = zone.Height;
                }
            }
            else
            {
                baseX = zone?.SpawnX ?? _svc.Merchant.MerchantX;
                baseY = zone?.SpawnY ?? _svc.Merchant.MerchantY;
                mapW = zone?.Width ?? _svc.World.Map.Width;
                mapH = zone?.Height ?? _svc.World.Map.Height;
            }

            var zoneMap = _svc.Zones.GetMap(pl.CurrentZoneId);
            int sx, sy;
            int attempts = 0;
            do
            {
                sx = baseX + _svc.World.NextRandom(Balance.RespawnJitterMin, Balance.RespawnJitterMax);
                sy = baseY + _svc.World.NextRandom(Balance.RespawnJitterMin, Balance.RespawnJitterMax);
                sx = Math.Clamp(sx, 0, mapW - 1);
                sy = Math.Clamp(sy, 0, mapH - 1);
                attempts++;
            }
            while (zoneMap?.IsObstacle(sx, sy) == true && attempts < 20);

            pl.X = sx;
            pl.Y = sy;
        }

        var client = _svc.World.FindClientByPlayer(pl);
        if (client != null)
        {
            await _svc.Hub.SendZoneTransition(client, pl);
            await _svc.ChatTo(client, ChatChannel.System, "Система", "Вы возродились!");
            await _svc.Hub.SendToClient(client, GameMessage.SystemChat("Вы возродились!"));
        }
        await _svc.Hub.BroadcastMapAsync();
        await _svc.Party.SendUpdateForAsync(pl);
        if (client != null)
            await _svc.Hub.SendStatusAsync(client, pl);
    }

    public async Task DeathTimerTick()
    {
        foreach (var pl in _svc.World.GetPlayersSnapshot())
        {
            if (pl.IsDead && (DateTime.UtcNow - pl.DeathTime).TotalMilliseconds >= Balance.DeathDelayMs)
            {
                if (pl.CurrentZoneId.StartsWith("instance:"))
                {
                    var inst = _svc.Instances.FindInstanceByPlayer(pl);
                    if (inst != null)
                    {
                        int spawnX = inst._spawnX > 0 ? inst._spawnX : inst.Template.SpawnX + inst.OffsetX;
                        int spawnY = inst._spawnY > 0 ? inst._spawnY : inst.Template.SpawnY + inst.OffsetY;
                        await RespawnPlayer(pl, spawnX, spawnY);
                    }
                    else
                    {
                        await RespawnPlayer(pl);
                    }
                }
                else
                {
                    await RespawnPlayer(pl);
                }
            }
        }
    }
}

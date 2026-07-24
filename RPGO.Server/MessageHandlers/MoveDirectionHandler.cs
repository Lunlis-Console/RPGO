using RPGGame.Server.Network;
using RPGGame.Shared.Commands;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;
using System.Text.Json;

namespace RPGGame.Server.MessageHandlers;

public class MoveDirectionHandler : BaseHandler
{
    public MoveDirectionHandler(GameWorld world, INetworkHub hub) : base(world, hub) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;
        if (player.IsDead) return;

        player.Movement.Stop(); // отменяем путь при ручном управлении

        // Ручное перемещение отменяет цель атаки
        if (player.Combat.HasTarget)
        {
            player.Combat.Cancel();
            player.QueuedSkillIds.Clear();
            await UseSkillHandler.SendSkillQueue(connection, player);
            await SendToClient(connection, new GameMessage
            {
                Type = "combat_state",
                Data = new { InCombat = false, TargetId = (string?)null, TargetName = (string?)null, TargetHp = 0, TargetMaxHp = 0 }
            });
        }

        var moveData = JsonSerializer.Deserialize<MoveDirectionCommand>(JsonSerializer.Serialize(message.Data));
        if (moveData == null) return;

        int moveIntervalMs = Balance.MoveIntervalMs(player.Speed);
        if ((DateTime.UtcNow - player.Movement.LastMoveTime).TotalMilliseconds < moveIntervalMs)
            return; // слишком быстро, игнорируем перемещение

        var zoneMap = Program.Services.Zones.GetOrCreateMap(player.CurrentZoneId);
        int newX = player.X;
        int newY = player.Y;

        switch (moveData.Direction)
        {
            case "up": newY--; break;
            case "down": newY++; break;
            case "left": newX--; break;
            case "right": newX++; break;
        }

        if (newX >= 0 && newX < zoneMap.Width && newY >= 0 && newY < zoneMap.Height)
        {
            player.X = newX;
            player.Y = newY;
            player.Facing = moveData.Direction;
            player.Movement.LastMoveTime = DateTime.UtcNow;
            Log.Debug($"{player.Name} переместился на ({player.X}, {player.Y})");

            // Проверка портала
            var portal = Program.Services.Zones.FindPortal(player.CurrentZoneId, player.X, player.Y);
            if (portal != null)
            {
                await HandleZoneTransition(connection, player, portal);
                return;
            }

            await BroadcastMapAsync();
        }
        else
        {
            await SendToClient(connection, new GameMessage
            {
                Type = "chat",
                Data = new { Name = "Система", Text = "Вы у края мира!" }
            });
        }
    }

    private async Task HandleZoneTransition(ClientConnection connection, Player player, Shared.Models.WorldPortal portal)
    {
        string fromZone = player.CurrentZoneId;
        player.CurrentZoneId = portal.ToZone;
        player.X = portal.ToX;
        player.Y = portal.ToY;
        player.Movement.Stop();
        player.Combat.Cancel();
        player.QueuedSkillIds.Clear();

        var targetZone = Program.Services.Zones.GetZone(portal.ToZone);
        string zoneName = targetZone?.Name ?? portal.ToZone;
        Log.Info($"{player.Name} перешёл из зоны '{fromZone}' в '{portal.ToZone}' ({portal.ToX},{portal.ToY})");

        await SendToClient(connection, new GameMessage
        {
            Type = "zone_transition",
            Data = new { ZoneId = portal.ToZone, ZoneName = zoneName, X = portal.ToX, Y = portal.ToY, PvPEnabled = targetZone?.PvpEnabled ?? false }
        });
        await SendToClient(connection, new GameMessage
        {
            Type = "chat",
            Data = new { Name = "Система", Text = $"Вы вошли в зону: {zoneName}{(targetZone?.PvpEnabled == true ? " [PvP]" : "")}" }
        });
        await BroadcastMapAsync();
    }
}

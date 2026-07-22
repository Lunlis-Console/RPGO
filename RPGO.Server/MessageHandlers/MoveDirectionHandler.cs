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

        int newX = player.X;
        int newY = player.Y;

        switch (moveData.Direction)
        {
            case "up": newY--; break;
            case "down": newY++; break;
            case "left": newX--; break;
            case "right": newX++; break;
        }

        if (newX >= 0 && newX < World.Map.Width && newY >= 0 && newY < World.Map.Height)
        {
            player.X = newX;
            player.Y = newY;
            player.Facing = moveData.Direction;
            player.Movement.LastMoveTime = DateTime.UtcNow;
            Log.Debug($"{player.Name} переместился на ({player.X}, {player.Y})");
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
}

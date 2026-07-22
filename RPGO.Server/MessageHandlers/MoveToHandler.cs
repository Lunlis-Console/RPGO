using RPGGame.Server.Network;
using RPGGame.Shared.Commands;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;
using System.Text.Json;

namespace RPGGame.Server.MessageHandlers;

public class MoveToHandler : BaseHandler
{
    public MoveToHandler(GameWorld world, INetworkHub hub) : base(world, hub) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;
        if (player.IsDead) return;

        if (TradeManager.IsInTrade(player))
        {
            var session = TradeManager.GetSession(player.Id);
            if (session != null) TradeManager.CancelSession(session, $"{player.Name} начал движение");
            player.IsTrading = false;
            await SendToClient(connection, new GameMessage
            {
                Type = "trade_close",
                Data = new { Message = "Обмен отменён: вы отошли." }
            });
            var other = session?.GetOther(player);
            if (other != null)
            {
                other.IsTrading = false;
                var otherConn = World.FindClientByPlayer(other);
                if (otherConn != null)
                    await SendToClient(otherConn, new GameMessage
                    {
                        Type = "trade_close",
                        Data = new { Message = $"Обмен отменён: {player.Name} отошёл." }
                    });
            }
            return;
        }

        // Клик по карте отменяет цель атаки
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

        var moveToData = JsonSerializer.Deserialize<MoveToCommand>(JsonSerializer.Serialize(message.Data));
        if (moveToData == null) return;

        var path = Pathfinding.FindPath(player.X, player.Y, moveToData.X, moveToData.Y);
        player.Movement.SetPath(path);

        if (path.Count == 0 && !(player.X == moveToData.X && player.Y == moveToData.Y))
        {
            await SendError(connection, ErrorCodes.PathNotFound, "Путь не найден!");
        }
    }
}

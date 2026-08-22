using LostAndDivine.Server.Network;
using LostAndDivine.Shared.Commands;
using LostAndDivine.Server.Services;
using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;
using System.Text.Json;

namespace LostAndDivine.Server.MessageHandlers;

public class MoveToHandler : BaseHandler
{
    public MoveToHandler(GameServices svc) : base(svc) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;
        if (player.IsDead) return;
        if (Svc.Debuffs.HasDebuff(player, DebuffType.Stun)) return;
        if (Svc.Debuffs.HasDebuff(player, DebuffType.Root)) return;

        if (Svc.Trade.IsInTrade(player))
        {
            var session = Svc.Trade.GetSession(player.Id);
            if (session != null) Svc.Trade.CancelSession(session, $"{player.Name} начал движение");
            player.IsTrading = false;
            await SendToClient(connection, new GameMessage
            {
                Type = GameMessageType.TradeClose,
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
                        Type = GameMessageType.TradeClose,
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
            await UseSkillHandler.SendSkillQueue(connection, player, Hub);
            await SendToClient(connection, new GameMessage
            {
                Type = GameMessageType.CombatState,
                Data = new { InCombat = false, TargetId = (string?)null, TargetName = (string?)null, TargetHp = 0, TargetMaxHp = 0 }
            });
        }

        var moveToData = JsonSerializer.Deserialize<MoveToCommand>(JsonSerializer.Serialize(message.Data));
        if (moveToData == null) return;

        var path = Svc.Pathfinding.FindPath(player.X, player.Y, moveToData.X, moveToData.Y, player.CurrentZoneId);
        player.Movement.SetPath(path);

        if (path.Count == 0 && !(player.X == moveToData.X && player.Y == moveToData.Y))
        {
            await SendError(connection, ErrorCodes.PathNotFound, "Путь не найден!");
        }
    }
}

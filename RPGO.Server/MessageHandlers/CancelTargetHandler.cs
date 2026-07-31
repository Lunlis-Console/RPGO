using RPGGame.Server.Network;
using RPGGame.Server.Services;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;
using System.Text.Json;

namespace RPGGame.Server.MessageHandlers;

public class CancelTargetHandler : BaseHandler
{
    public CancelTargetHandler(GameServices svc) : base(svc) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;

        if (player.Combat.HasTarget)
        {
            var prevTarget = Svc.Monsters.FindMonsterById(player.Combat.TargetMonsterId!.Value);
            Log.Debug($"{player.Name} отменил цель: {prevTarget?.Name ?? "?"}");
            player.Combat.Cancel();
            player.QueuedSkillIds.Clear();
            await UseSkillHandler.SendSkillQueue(connection, player, Hub);
            await SendToClient(connection, new GameMessage
            {
                Type = "combat_state",
                Data = new { InCombat = false, TargetId = (string?)null, TargetName = (string?)null, TargetHp = 0, TargetMaxHp = 0 }
            });
        }
    }
}

using RPGGame.Server.Network;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;
using System.Text.Json;

namespace RPGGame.Server.MessageHandlers;

public class SelectTargetHandler : BaseHandler
{
    public SelectTargetHandler(GameWorld world, INetworkHub hub) : base(world, hub) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;
        if (message.Data is not JsonElement selEl) return;

        string? monsterIdStr = selEl.TryGetProperty("MonsterId", out var midProp) ? midProp.GetString() : null;
        if (monsterIdStr == null || !Guid.TryParse(monsterIdStr, out Guid monsterId)) return;

        var target = MonsterManager.FindMonsterById(monsterId);
        if (target == null)
        {
            await SendToClient(connection, new GameMessage
            {
                Type = "error",
                Data = new { Code = ErrorCodes.TargetNotFound, Message = "Цель не найдена!" }
            });
            return;
        }

        if (target.Health <= 0)
        {
            await SendError(connection, ErrorCodes.TargetDead, "Этот монстр уже мёртв!");
            return;
        }

        player.Combat.Enter(target.Id, player.Movement);
        // Очередь сбрасывается при смене цели; прекаст (если был) применится первым ударом.
        player.QueuedSkillIds.Clear();
        Log.Debug($"{player.Name} выбрал цель: {target.Name} ({target.X},{target.Y})");
        await SendToClient(connection, new GameMessage
        {
            Type = "combat_state",
            Data = new
            {
                InCombat = true,
                TargetId = target.Id.ToString(),
                TargetName = target.Name,
                TargetHp = target.Health,
                TargetMaxHp = target.MaxHealth,
                TargetX = target.X,
                TargetY = target.Y
            }
        });
        await SendToClient(connection, new GameMessage
        {
            Type = "chat",
            Data = new { Name = "Бой", Text = $"Цель: {target.Name} [{target.Level}] ({target.Health}/{target.MaxHealth}) — автоатака начнётся при приближении." }
        });
    }
}

using RPGGame.Server.Network;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;
using System.Text.Json;

namespace RPGGame.Server.MessageHandlers;

public class InteractTargetHandler : BaseHandler
{
    public InteractTargetHandler(GameWorld world, INetworkHub hub) : base(world, hub) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;
        if (message.Data is not JsonElement interEl) return;

        string? entityType = interEl.TryGetProperty("Type", out var etProp) ? etProp.GetString() : null;
        int targetX = interEl.TryGetProperty("X", out var txProp) ? txProp.GetInt32() : -1;
        int targetY = interEl.TryGetProperty("Y", out var tyProp) ? tyProp.GetInt32() : -1;
        string? monsterIdStr = interEl.TryGetProperty("MonsterId", out var midProp) ? midProp.GetString() : null;

        if (entityType == null || targetX < 0 || targetY < 0) return;

        player.Movement.Stop();
        player.Interaction.Clear();

        if (entityType == "monster")
        {
            Monster? interMonster = null;
            if (monsterIdStr != null && Guid.TryParse(monsterIdStr, out Guid interMonsterId))
                interMonster = Program.Services.Monsters.FindMonsterById(interMonsterId);
            if (interMonster == null)
                interMonster = Program.Services.Monsters.FindMonsterAt(targetX, targetY);

            if (interMonster == null || interMonster.Health <= 0)
            {
                await SendError(connection, ErrorCodes.TargetNotFound, "Монстр не найден!");
                return;
            }

            player.Combat.Enter(interMonster.Id, player.Movement);

            var w = player.Equipment[EquipmentSlots.RightHand];
            Log.Debug($"[Interact] {player.Name} -> {interMonster.Name}: weapon='{w?.Name ?? "null"}' AttackRange={w?.AttackRange ?? -1} TemplateId='{w?.TemplateId ?? ""}'");

            Log.Debug($"{player.Name} вступил в бой с {interMonster.Name} ({interMonster.X},{interMonster.Y})");

            await SendToClient(connection, new GameMessage
            {
                Type = "combat_state",
                Data = new
                {
                    InCombat = true,
                    TargetId = interMonster.Id.ToString(),
                    TargetName = interMonster.Name,
                    TargetHp = interMonster.Health,
                    TargetMaxHp = interMonster.MaxHealth,
                    TargetX = interMonster.X,
                    TargetY = interMonster.Y
                }
            });
            await SendToClient(connection, new GameMessage
            {
                Type = "chat",
                Data = new { Name = "Бой", Text = $"Бой: {interMonster.Name} [{interMonster.Level}] ({interMonster.Health}/{interMonster.MaxHealth})" }
            });
            await BroadcastMapAsync();
            return;
        }

        // Не-монстры: магазин, доска, собиратель
        player.Combat.Cancel();

        int distToTarget = Math.Abs(player.X - targetX) + Math.Abs(player.Y - targetY);
        if (distToTarget <= Balance.InteractRange)
        {
            player.Interaction.Begin(entityType, targetX, targetY, null);
            await ProcessPendingInteraction(player, entityType);
            await BroadcastMapAsync();
            return;
        }

        int[] dx = { 0, 0, -1, 1 };
        int[] dy = { -1, 1, 0, 0 };
        int bestX = -1, bestY = -1;
        int bestDist = int.MaxValue;

        for (int i = 0; i < 4; i++)
        {
            int nx = targetX + dx[i];
            int ny = targetY + dy[i];
            if (nx < 0 || nx >= World.Map.Width || ny < 0 || ny >= World.Map.Height) continue;
            if (nx == Program.Services.Merchant.MerchantX && ny == Program.Services.Merchant.MerchantY) continue;
            if (nx == Program.Services.Quests.BoardX && ny == Program.Services.Quests.BoardY) continue;
            if (Program.Services.Monsters.FindMonsterAt(nx, ny) != null) continue;

            int dist = Math.Abs(nx - player.X) + Math.Abs(ny - player.Y);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestX = nx;
                bestY = ny;
            }
        }

        if (bestX < 0)
        {
            await SendToClient(connection, new GameMessage
            {
                Type = "error",
                Data = new { Code = ErrorCodes.NoFreeCell, Message = "Нет свободной клетки рядом с целью." }
            });
            return;
        }

        var path = Program.Services.Pathfinding.FindPath(player.X, player.Y, bestX, bestY);
        if (path.Count > 0)
        {
            player.Movement.SetPath(path);
            player.Interaction.Begin(entityType, targetX, targetY, null);
            Log.Debug($"{player.Name} идёт к {entityType} ({targetX},{targetY}), путь {path.Count} шагов");
        }
        else
        {
            await SendToClient(connection, new GameMessage
            {
                Type = "chat",
                Data = new { Name = "Система", Text = "Путь не найден!" }
            });
        }
    }
}

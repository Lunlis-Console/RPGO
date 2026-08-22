using LostAndDivine.Server.Network;
using LostAndDivine.Server.Services;
using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;
using System.Text.Json;

namespace LostAndDivine.Server.MessageHandlers;

public class InteractTargetHandler : BaseHandler
{
    public InteractTargetHandler(GameServices svc) : base(svc) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;
        if (message.Data is not JsonElement interEl) return;

        string? entityType = interEl.TryGetProperty("Type", out var etProp) ? etProp.GetString() : null;
        int targetX = interEl.TryGetProperty("X", out var txProp) ? txProp.GetInt32() : -1;
        int targetY = interEl.TryGetProperty("Y", out var tyProp) ? tyProp.GetInt32() : -1;
        string? monsterIdStr = interEl.TryGetProperty("MonsterId", out var midProp) ? midProp.GetString() : null;
        string? playerIdStr = interEl.TryGetProperty("PlayerId", out var pidProp) ? pidProp.GetString() : null;

        if (entityType == null || targetX < 0 || targetY < 0) return;

        player.Movement.Stop();
        player.Interaction.Clear();

        if (entityType == "monster")
        {
            Monster? interMonster = null;
            if (monsterIdStr != null && Guid.TryParse(monsterIdStr, out Guid interMonsterId))
                interMonster = Svc.Monsters.FindMonsterById(interMonsterId);
            if (interMonster == null)
                interMonster = Svc.Monsters.FindMonsterAt(targetX, targetY);

            if (interMonster == null || interMonster.Health <= 0)
            {
                await SendError(connection, ErrorCodes.TargetNotFound, "Монстр не найден!");
                return;
            }

            player.Combat.EnterMonster(interMonster.Id, player.Movement);

            var w = player.Equipment[EquipmentSlots.RightHand];
            Log.Debug($"[Interact] {player.Name} -> {interMonster.Name}: weapon='{w?.Name ?? "null"}' AttackRange={w?.AttackRange ?? -1} TemplateId='{w?.TemplateId ?? ""}'");

            Log.Debug($"{player.Name} вступил в бой с {interMonster.Name} ({interMonster.X},{interMonster.Y})");

            await SendToClient(connection, new GameMessage
            {
                Type = GameMessageType.CombatState,
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
                Type = GameMessageType.Chat,
                Data = new { Name = "Бой", Text = $"Бой: {interMonster.Name} [{interMonster.Level}] ({interMonster.Health}/{interMonster.MaxHealth})" }
            });
            await BroadcastMapAsync();
            return;
        }

        if (entityType == "player")
        {
            // Находим цель
            Player? targetPlayer = null;
            if (playerIdStr != null && Guid.TryParse(playerIdStr, out Guid pid))
                targetPlayer = Svc.World.GetPlayersSnapshot().FirstOrDefault(p => p.Id == pid && p.CurrentZoneId == player.CurrentZoneId);
            if (targetPlayer == null)
                targetPlayer = Svc.World.GetPlayersSnapshot().FirstOrDefault(p => p.X == targetX && p.Y == targetY && p.CurrentZoneId == player.CurrentZoneId);

            if (targetPlayer == null || targetPlayer.IsDead)
            {
                await SendError(connection, ErrorCodes.TargetNotFound, "Игрок не найден!");
                return;
            }

            if (targetPlayer.Id == player.Id)
            {
                await SendError(connection, ErrorCodes.TargetNotFound, "Нельзя выбрать себя!");
                return;
            }

            bool isPvp = Svc.Zones.IsPvPEnabled(player.CurrentZoneId);

            if (isPvp)
            {
                player.Combat.EnterPlayer(targetPlayer.Id, player.Movement);
                await SendToClient(connection, new GameMessage
                {
                    Type = GameMessageType.CombatState,
                    Data = new
                    {
                        InCombat = true,
                        TargetId = targetPlayer.Id.ToString(),
                        TargetName = targetPlayer.Name,
                        TargetHp = targetPlayer.Health,
                        TargetMaxHp = targetPlayer.MaxHealth + targetPlayer.Equipment.GetBonusMaxHealth(),
                        TargetX = targetPlayer.X,
                        TargetY = targetPlayer.Y,
                        IsPvP = true
                    }
                });
                await SendToClient(connection, new GameMessage
                {
                    Type = GameMessageType.Chat,
                    Data = new { Name = "Бой", Text = $"PvP бой: {targetPlayer.Name} [{targetPlayer.Level}] ({targetPlayer.Health}/{targetPlayer.MaxHealth + targetPlayer.Equipment.GetBonusMaxHealth()})" }
                });
            }

            // Шлём текущие дебаффы цели (в любом режиме)
            await Svc.Combat.SendTargetPlayerDebuffUpdateAsync(targetPlayer, connection);
            await BroadcastMapAsync();
            return;
        }

        // Не-монстры и не-игроки: магазин, доска, собиратель
        player.Combat.Cancel();

        int distToTarget = Math.Abs(player.X - targetX) + Math.Abs(player.Y - targetY);
        if (distToTarget <= Balance.InteractRange)
        {
            player.Interaction.Begin(entityType, targetX, targetY, null);
            await ProcessPendingInteraction(player, entityType);
            // Снимаем pending: иначе в следующем тике движения взаимодействие обработается повторно
            // (например, дверь телепортнёт игрока обратно на исходную клетку).
            player.Interaction.Clear();
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
            var zoneMap = Svc.Zones.GetOrCreateMap(player.CurrentZoneId);
            if (nx < 0 || nx >= zoneMap.Width || ny < 0 || ny >= zoneMap.Height) continue;
            if (zoneMap.IsObstacle(nx, ny)) continue;
            var zoneNpcs = Svc.Zones.GetTiledNpcs(player.CurrentZoneId);
            if (zoneNpcs.Any(n => n.X == nx && n.Y == ny && (n.Type == "merchant" || n.Type == "board" || n.Type == "blacksmith"))) continue;
            if (Svc.Monsters.FindMonsterAt(nx, ny) != null) continue;

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
            await SendError(connection, ErrorCodes.NoFreeCell, "Нет свободной клетки рядом с целью.");
            return;
        }

        var path = Svc.Pathfinding.FindPath(player.X, player.Y, bestX, bestY, player.CurrentZoneId);
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
                Type = GameMessageType.Chat,
                Data = new { Name = "Система", Text = "Путь не найден!" }
            });
        }
    }
}

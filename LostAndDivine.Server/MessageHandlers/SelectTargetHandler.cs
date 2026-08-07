using LostAndDivine.Server.Network;
using LostAndDivine.Server.Services;
using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;
using System.Text.Json;

namespace LostAndDivine.Server.MessageHandlers;

public class SelectTargetHandler : BaseHandler
{
    public SelectTargetHandler(GameServices svc) : base(svc) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;
        if (player.IsDead) return;
        if (message.Data is not JsonElement selEl) return;

        string? playerIdStr = selEl.TryGetProperty("PlayerId", out var pidProp) ? pidProp.GetString() : null;
        if (playerIdStr != null && Guid.TryParse(playerIdStr, out Guid playerTargetId))
        {
            Player? targetPlayer = Svc.World.GetPlayersSnapshot()
                .FirstOrDefault(p => p.Id == playerTargetId && p.CurrentZoneId == player.CurrentZoneId);
            if (targetPlayer == null || targetPlayer.IsDead)
            {
                await SendError(connection, ErrorCodes.TargetNotFound, "����� �� ������!");
                return;
            }
            await Svc.Combat.SendTargetPlayerDebuffUpdateAsync(targetPlayer, connection);
            return;
        }

        string? monsterIdStr = selEl.TryGetProperty("MonsterId", out var midProp) ? midProp.GetString() : null;
        if (monsterIdStr == null || !Guid.TryParse(monsterIdStr, out Guid monsterId)) return;

        var target = Svc.Monsters.FindMonsterById(monsterId);
        if (target == null)
        {
            await SendError(connection, ErrorCodes.TargetNotFound, "���� �� �������!");
            return;
        }

        if (target.Health <= 0)
        {
            await SendError(connection, ErrorCodes.TargetDead, "���� ������ ��� ����!");
            return;
        }

        bool wasInCombat = player.Combat.InCombat;
        player.Combat.Enter(target.Id, player.Movement);
        // ��� ������������ ���� � ��� � ����� �������. ������� ��� ������ ����� �����������.
        if (wasInCombat) player.QueuedSkillIds.Clear();
        Log.Debug($"{player.Name} ������ ����: {target.Name} ({target.X},{target.Y})");
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
                TargetY = target.Y,
                TargetDebuffs = target.GetDebuffsSnapshot().Select(d => new
                {
                    Type = d.Type.ToString(),
                    d.DisplayName,
                    d.Description,
                    Value = Math.Round(d.Value, 2),
                    d.RemainingMs,
                    DurationMs = d.DurationMs
                }).ToList()
            }
        });
        await SendToClient(connection, new GameMessage
        {
            Type = "chat",
            Data = new { Name = "���", Text = $"����: {target.Name} [{target.Level}] ({target.Health}/{target.MaxHealth}) � ��������� ������� ��� �����������." }
        });
    }
}

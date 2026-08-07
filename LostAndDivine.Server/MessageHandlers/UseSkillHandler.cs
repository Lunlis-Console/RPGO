using LostAndDivine.Server.Network;
using LostAndDivine.Server.Services;
using LostAndDivine.Shared;
using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;
using System.Text.Json;

namespace LostAndDivine.Server.MessageHandlers;

public class UseSkillHandler : BaseHandler
{
    private static readonly HashSet<string> InstantSkills = new() { SkillIds.Flurry };

    public UseSkillHandler(GameServices svc) : base(svc) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;
        if (Svc.Debuffs.HasDebuff(player, DebuffType.Stun))
        {
            await SendToClient(connection, new GameMessage
            {
                Type = "chat",
                Data = new { Name = "���", Text = "�� �������� � �� ������ ��������� ������!" }
            });
            return;
        }
        if (message.Data is not JsonElement el) return;

        string? skillId = el.TryGetProperty("SkillId", out var sid) ? sid.GetString() : null;
        if (skillId == null) return;

        var skill = DatabaseManager.GetSkill(skillId);
        if (skill == null)
        {
            await SendError(connection, ErrorCodes.SkillNotFound, "����� �� ������.");
            return;
        }

        if (!player.LearnedSkills.Contains(skill.Id))
        {
            await SendToClient(connection, new GameMessage
            {
                Type = "chat",
                Data = new { Name = "���", Text = $"�{skill.Name}� �� ������." }
            });
            return;
        }

        if (!player.Combat.InCombat)
        {
            if (InstantSkills.Contains(skill.Id))
            {
                // ���������� ���� � ��������� ����� ��� ���
                if (player.LastSkillUse.TryGetValue(skill.Id, out var last)
                    && (DateTime.UtcNow - last).TotalMilliseconds < skill.CooldownMs * player.GetSkillRankCdMult(skill.Id))
                {
                    await SendToClient(connection, new GameMessage
                    {
                        Type = "chat",
                        Data = new { Name = "���", Text = $"�{skill.Name}� ��� �� �����������." }
                    });
                    return;
                }

                if (player.Mana < skill.MpCost)
                {
                    await SendToClient(connection, new GameMessage
                    {
                        Type = "chat",
                        Data = new { Name = "���", Text = $"�{skill.Name}�: ������������ ���� ({player.Mana}/{skill.MpCost})." }
                    });
                    return;
                }

                player.Mana = Math.Max(0, player.Mana - skill.MpCost);
                player.LastSkillUse[skill.Id] = DateTime.UtcNow;

                if (skill.Id == "SK0002")
                {
                    var buff = ActiveDebuff.Create(DebuffType.AttackSpeedBonus, Balance.AttackSpeedBonusValue,
                        Balance.AttackSpeedBonusDurationMs, "skill", "�����������",
                        $"����������� �������� ����� �� {(int)(Balance.AttackSpeedBonusValue * 100)}%");
                    Svc.Debuffs.ApplyDebuff(player, buff);

                    await Svc.Hub.SendToAllAsync(new GameMessage
                    {
                        Type = "player_attack",
                        Data = new { PlayerName = player.Name, Hand = "main", SkillId = "SK0002", BuffDurationMs = Balance.AttackSpeedBonusDurationMs }
                    });
                }

                await SendToClient(connection, new GameMessage
                {
                    Type = "chat",
                    Data = new { Name = "���", Text = $"������� ����� �{skill.Name}�!" }
                });
                int effectiveCd = (int)(skill.CooldownMs * player.GetSkillRankCdMult(skill.Id));
                await SendToClient(connection, new GameMessage
                {
                    Type = "skill_cooldown",
                    Data = new { SkillId = skill.Id, RemainingMs = effectiveCd, TotalMs = effectiveCd }
                });
                await Svc.Hub.SendStatusAsync(connection, player);
                return;
            }

            // �������� ���� ����� ����������� � �������
            if (player.Mana < skill.MpCost)
            {
                await SendToClient(connection, new GameMessage
                {
                    Type = "chat",
                    Data = new { Name = "���", Text = $"�{skill.Name}�: ������������ ���� ({player.Mana}/{skill.MpCost})." }
                });
                return;
            }

            // ������ �����: ������� ������ ������ (��������, �� ���������).
            player.QueuedSkillIds.Clear();
            player.QueuedSkillIds.Add(skill.Id);
            await SendToClient(connection, new GameMessage
            {
                Type = "chat",
                Data = new { Name = "���", Text = $"����� �{skill.Name}� ���������� � ���������� � ������ ���." }
            });
            await SendSkillQueue(connection, player, Hub);
            return;
        }

        // � ���: ��������� � ����� �������, ��� ������.
        if (player.Mana < skill.MpCost)
        {
            await SendToClient(connection, new GameMessage
            {
                Type = "chat",
                Data = new { Name = "���", Text = $"�{skill.Name}�: ������������ ���� ({player.Mana}/{skill.MpCost})." }
            });
            return;
        }

        if (player.QueuedSkillIds.Contains(skill.Id))
        {
            await SendToClient(connection, new GameMessage
            {
                Type = "chat",
                Data = new { Name = "���", Text = $"�{skill.Name}� ��� � �������." }
            });
            return;
        }

        player.QueuedSkillIds.Add(skill.Id);
        await SendToClient(connection, new GameMessage
        {
            Type = "chat",
            Data = new { Name = "���", Text = $"�{skill.Name}� �������� � ������� ({player.QueuedSkillIds.Count} � �������)." }
        });
        await SendSkillQueue(connection, player, Hub);
    }

    public static async Task SendSkillQueue(ClientConnection connection, Player player, INetworkHub hub)
    {
        await hub.SendToClient(connection, new GameMessage
        {
            Type = "skill_queue",
            Data = new { Skills = player.QueuedSkillIds.ToList() }
        });
    }
}

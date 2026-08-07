using LostAndDivine.Server.Network;
using LostAndDivine.Server.Services;
using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;
using System.Text.Json;

namespace LostAndDivine.Server.MessageHandlers;

public class AllocateSkillHandler : BaseHandler
{
    public AllocateSkillHandler(GameServices svc) : base(svc) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;
        if (message.Data is not JsonElement el) return;

        string? skillId = el.TryGetProperty("SkillId", out var idProp) ? idProp.GetString() : null;
        if (skillId == null) return;

        var allSkills = DatabaseManager.LoadSkills();
        var skill = allSkills.FirstOrDefault(s => s.Id == skillId);
        if (skill == null)
        {
            await SendError(connection, ErrorCodes.InvalidRequest, "����� �� ������!");
            return;
        }

        bool alreadyLearned = player.LearnedSkills.Contains(skillId);
        int currentRank = player.GetSkillRank(skillId);

        // ������� (��� ������ + ���� ���� �����)
        if (alreadyLearned)
        {
            if (currentRank >= skill.MaxRank)
            {
                await SendError(connection, ErrorCodes.InvalidRequest, $"����� �{skill.Name}� ��� ������������� ����� ({skill.MaxRank})!");
                return;
            }
        }
        else
        {
            // �������� ������� ��������
            if (player.Level < skill.MinLevel)
            {
                await SendError(connection, ErrorCodes.InvalidRequest, $"��������� ������� {skill.MinLevel}!");
                return;
            }
            if (!string.IsNullOrEmpty(skill.ParentId) && !player.LearnedSkills.Contains(skill.ParentId))
            {
                var parent = allSkills.FirstOrDefault(s => s.Id == skill.ParentId);
                string parentName = parent?.Name ?? skill.ParentId;
                await SendError(connection, ErrorCodes.InvalidRequest, $"������� ������� �{parentName}�!");
                return;
            }
        }

        if (player.SkillPoints < skill.SkillPointCost)
        {
            await SendError(connection, ErrorCodes.InvalidRequest, $"������������ ����� �������! �����: {skill.SkillPointCost}, ����: {player.SkillPoints}");
            return;
        }

        player.SkillPoints -= skill.SkillPointCost;
        int newRank = alreadyLearned ? currentRank + 1 : 1;

        if (!alreadyLearned)
            player.LearnedSkills.Add(skillId);
        player.SkillRanks[skillId] = newRank;

        Log.Info($"{player.Name} ������� �{skill.Name}� �� ����� {newRank}/{skill.MaxRank}. �����: {player.SkillPoints}");
        Svc.Persistence.EnqueueSave(player);

        await SendToClient(connection, new GameMessage
        {
            Type = "chat",
            Data = new { Name = "�������", Text = $"�{skill.Name}� � ���� {newRank}/{skill.MaxRank}! �������� �����: {player.SkillPoints}" }
        });
        await SendInventoryAndStatus(connection, player);
        await Hub.SendSkills(connection);
    }
}

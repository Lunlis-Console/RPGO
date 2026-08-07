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
            await SendError(connection, ErrorCodes.InvalidRequest, "Навык не найден!");
            return;
        }

        bool alreadyLearned = player.LearnedSkills.Contains(skillId);
        int currentRank = player.GetSkillRank(skillId);

        // Апгрейд (уже изучен + есть куда расти)
        if (alreadyLearned)
        {
            if (currentRank >= skill.MaxRank)
            {
                await SendError(connection, ErrorCodes.InvalidRequest, $"Навык «{skill.Name}» уже максимального ранга ({skill.MaxRank})!");
                return;
            }
        }
        else
        {
            // Проверка условий изучения
            if (player.Level < skill.MinLevel)
            {
                await SendError(connection, ErrorCodes.InvalidRequest, $"Требуется уровень {skill.MinLevel}!");
                return;
            }
            if (!string.IsNullOrEmpty(skill.ParentId) && !player.LearnedSkills.Contains(skill.ParentId))
            {
                var parent = allSkills.FirstOrDefault(s => s.Id == skill.ParentId);
                string parentName = parent?.Name ?? skill.ParentId;
                await SendError(connection, ErrorCodes.InvalidRequest, $"Сначала изучите «{parentName}»!");
                return;
            }
        }

        if (player.SkillPoints < skill.SkillPointCost)
        {
            await SendError(connection, ErrorCodes.InvalidRequest, $"Недостаточно очков навыков! Нужно: {skill.SkillPointCost}, есть: {player.SkillPoints}");
            return;
        }

        player.SkillPoints -= skill.SkillPointCost;
        int newRank = alreadyLearned ? currentRank + 1 : 1;

        if (!alreadyLearned)
            player.LearnedSkills.Add(skillId);
        player.SkillRanks[skillId] = newRank;

        Log.Info($"{player.Name} улучшил «{skill.Name}» до ранга {newRank}/{skill.MaxRank}. Очков: {player.SkillPoints}");
        Svc.Persistence.EnqueueSave(player);

        await SendToClient(connection, new GameMessage
        {
            Type = "chat",
            Data = new { Name = "Система", Text = $"«{skill.Name}» — ранг {newRank}/{skill.MaxRank}! Осталось очков: {player.SkillPoints}" }
        });
        await SendInventoryAndStatus(connection, player);
        await Hub.SendSkills(connection);
    }
}

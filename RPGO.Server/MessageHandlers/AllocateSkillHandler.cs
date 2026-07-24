using RPGGame.Server.Network;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;
using System.Text.Json;

namespace RPGGame.Server.MessageHandlers;

public class AllocateSkillHandler : BaseHandler
{
    public AllocateSkillHandler(GameWorld world, INetworkHub hub) : base(world, hub) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;
        if (message.Data is not JsonElement el) return;

        string? skillId = el.TryGetProperty("SkillId", out var idProp) ? idProp.GetString() : null;
        if (skillId == null) return;

        if (player.LearnedSkills.Contains(skillId))
        {
            await SendError(connection, ErrorCodes.InvalidRequest, "Этот навык уже изучен!");
            return;
        }

        var allSkills = DatabaseManager.LoadSkills();
        var skill = allSkills.FirstOrDefault(s => s.Id == skillId);
        if (skill == null)
        {
            await SendError(connection, ErrorCodes.InvalidRequest, "Навык не найден!");
            return;
        }

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

        if (player.SkillPoints < skill.SkillPointCost)
        {
            await SendError(connection, ErrorCodes.InvalidRequest, $"Недостаточно очков навыков! Нужно: {skill.SkillPointCost}, есть: {player.SkillPoints}");
            return;
        }

        player.SkillPoints -= skill.SkillPointCost;
        player.LearnedSkills.Add(skillId);

        Log.Info($"{player.Name} изучил навык «{skill.Name}» ({skillId}). Очков: {player.SkillPoints}");
        DatabaseManager.SavePlayerProgress(player);

        await SendToClient(connection, new GameMessage
        {
            Type = "chat",
            Data = new { Name = "Система", Text = $"Изучен навык «{skill.Name}»! Осталось очков навыков: {player.SkillPoints}" }
        });
        await SendInventoryAndStatus(connection, player);
        await Hub.SendSkills(connection);
    }
}

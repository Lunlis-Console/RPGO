using RPGGame.Server.Network;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;
using System.Text.Json;

namespace RPGGame.Server.MessageHandlers;

public class UseSkillHandler : BaseHandler
{
    public UseSkillHandler(GameWorld world, INetworkHub hub) : base(world, hub) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;
        if (message.Data is not JsonElement el) return;

        string? skillId = el.TryGetProperty("SkillId", out var sid) ? sid.GetString() : null;
        if (skillId == null) return;

        var skill = DatabaseManager.GetSkill(skillId);
        if (skill == null)
        {
            await SendError(connection, ErrorCodes.SkillNotFound, "Навык не найден.");
            return;
        }

        if (!player.Combat.InCombat)
        {
            // Мирный режим: прекаст одного навыка (заменяем, не добавляем).
            player.QueuedSkillIds.Clear();
            player.QueuedSkillIds.Add(skill.Id);
            await SendToClient(connection, new GameMessage
            {
                Type = "chat",
                Data = new { Name = "Бой", Text = $"Навык «{skill.Name}» заготовлен — применится в начале боя." }
            });
            await SendSkillQueue(connection, player);
            return;
        }

        // В бою: добавляем в хвост очереди, без дублей.
        if (player.QueuedSkillIds.Contains(skill.Id))
        {
            await SendToClient(connection, new GameMessage
            {
                Type = "chat",
                Data = new { Name = "Бой", Text = $"«{skill.Name}» уже в очереди." }
            });
            return;
        }

        player.QueuedSkillIds.Add(skill.Id);
        await SendToClient(connection, new GameMessage
        {
            Type = "chat",
            Data = new { Name = "Бой", Text = $"«{skill.Name}» добавлен в очередь ({player.QueuedSkillIds.Count} в очереди)." }
        });
        await SendSkillQueue(connection, player);
    }

    public static async Task SendSkillQueue(ClientConnection connection, Player player)
    {
        await Program.Services.Hub.SendToClient(connection, new GameMessage
        {
            Type = "skill_queue",
            Data = new { Skills = player.QueuedSkillIds.ToList() }
        });
    }
}

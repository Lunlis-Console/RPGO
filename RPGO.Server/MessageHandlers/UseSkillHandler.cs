using RPGGame.Server.Network;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;
using System.Text.Json;

namespace RPGGame.Server.MessageHandlers;

public class UseSkillHandler : BaseHandler
{
    private static readonly HashSet<string> InstantSkills = new() { "SK0002" };

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
            if (InstantSkills.Contains(skill.Id))
            {
                // Мгновенный бафф — применяем сразу без боя
                if (player.LastSkillUse.TryGetValue(skill.Id, out var last)
                    && (DateTime.UtcNow - last).TotalMilliseconds < skill.CooldownMs)
                {
                    await SendToClient(connection, new GameMessage
                    {
                        Type = "chat",
                        Data = new { Name = "Бой", Text = $"«{skill.Name}» ещё на перезарядке." }
                    });
                    return;
                }

                if (player.Mana < skill.MpCost)
                {
                    await SendToClient(connection, new GameMessage
                    {
                        Type = "chat",
                        Data = new { Name = "Бой", Text = $"«{skill.Name}»: недостаточно маны ({player.Mana}/{skill.MpCost})." }
                    });
                    return;
                }

                player.Mana = Math.Max(0, player.Mana - skill.MpCost);
                player.LastSkillUse[skill.Id] = DateTime.UtcNow;

                if (skill.Id == "SK0002")
                {
                    var buff = ActiveDebuff.Create(DebuffType.AttackSpeedBonus, 0.30,
                        10000, "skill", "Проворность",
                        "Увеличивает скорость атаки на 30%");
                    Program.Services.Debuffs.ApplyDebuff(player, buff);
                }

                await SendToClient(connection, new GameMessage
                {
                    Type = "chat",
                    Data = new { Name = "Бой", Text = $"Применён навык «{skill.Name}»!" }
                });
                await SendToClient(connection, new GameMessage
                {
                    Type = "skill_cooldown",
                    Data = new { SkillId = skill.Id, RemainingMs = skill.CooldownMs, TotalMs = skill.CooldownMs }
                });
                await Program.Services.Hub.SendStatusAsync(connection, player);
                return;
            }

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

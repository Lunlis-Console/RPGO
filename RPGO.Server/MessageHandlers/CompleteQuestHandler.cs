using RPGGame.Server.Network;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;
using System.Text.Json;

namespace RPGGame.Server.MessageHandlers;

public class CompleteQuestHandler : BaseHandler
{
    public CompleteQuestHandler(GameWorld world, INetworkHub hub) : base(world, hub) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;
        if (message.Data is not JsonElement compEl) return;

        string? questId = compEl.ValueKind == JsonValueKind.String
            ? compEl.GetString()
            : compEl.TryGetProperty("QuestId", out var cqProp) ? cqProp.GetString() : null;

        if (!QuestManager.IsAtBoard(player.X, player.Y))
        {
            await SendError(connection, ErrorCodes.NotAtBoard, "Вернитесь к доске заданий, чтобы сдать задание.");
            return;
        }

        if (questId == null)
        {
            await SendError(connection, ErrorCodes.QuestNotSpecified, "Задание не указано.");
            return;
        }

        var prog = player.ActiveQuests.FirstOrDefault(q => q.QuestId == questId);
        var def = QuestManager.FindQuest(questId);
        if (prog == null || def == null)
        {
            await SendError(connection, ErrorCodes.QuestNotActive, "У вас нет этого задания.");
            return;
        }

        if (!prog.Completed)
        {
            await SendError(connection, ErrorCodes.QuestNotCompleted, $"Задание ещё не выполнено ({prog.Current}/{def.Target}).");
            return;
        }

        // Списываем предметы, нужные для сдачи квеста (collect)
        if (def.Type == "collect" && !string.IsNullOrEmpty(def.TargetItemId))
        {
            int toRemove = Math.Min(def.Target, player.Inventory.Count(i => i.Id == def.TargetItemId));
            for (int i = 0; i < toRemove; i++)
            {
                var item = player.Inventory.FirstOrDefault(it => it.Id == def.TargetItemId);
                if (item != null) player.Inventory.Remove(item);
            }
        }

        player.ActiveQuests.Remove(prog);
        player.Experience += def.XpReward;
        player.Gold += def.GoldReward;
        Log.Info($"{player.Name} сдал задание {def.Title}: +{def.XpReward} XP, +{def.GoldReward} золота");
        await SendToClient(connection, new GameMessage
        {
            Type = "chat",
            Data = new { Name = "Система", Text = $"Задание выполнено! {def.Title}. Награда: +{def.XpReward} опыта, +{def.GoldReward} золота." }
        });

        int xpNeeded = Balance.XpNeededForNextLevel(player.Level);
        if (player.Experience >= xpNeeded)
        {
            player.Level++;
            player.Experience -= xpNeeded;
            player.MaxHealth += Balance.MaxHealthPerLevel;
            player.Health = player.MaxHealth;
            player.AttributePoints += Balance.AttributePointsPerLevel;
            Log.Info($"{player.Name} повысил уровень до {player.Level}! +{Balance.AttributePointsPerLevel} очка атрибутов");
            await SendToClient(connection, new GameMessage
            {
                Type = "chat",
                Data = new { Name = "Система", Text = $"Уровень повышен! Вы теперь уровень {player.Level}! +{Balance.AttributePointsPerLevel} очка атрибутов. HP восстановлены." }
            });
        }

        await SendQuestLog(connection, player);
        await SendInventoryAndStatus(connection, player);
    }
}

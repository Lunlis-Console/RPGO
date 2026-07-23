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

        if (!Program.Services.Quests.IsAtBoard(player.X, player.Y))
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
        var def = Program.Services.Quests.FindQuest(questId);
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
            var records = player.Inventory.Where(i => i.TemplateId == def.TargetItemId || i.Id == def.TargetItemId).ToList();
            int available = records.Sum(i => i.Quantity);
            int toRemove = Math.Min(def.Target, available);
            foreach (var rec in records)
            {
                if (toRemove <= 0) break;
                int take = Math.Min(toRemove, rec.Quantity);
                InventoryHelper.RemoveFromRecord(player, rec.Id, take);
                toRemove -= take;
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

        if (player.TryLevelUp())
        {
            Log.Info($"{player.Name} повысил уровень до {player.Level}! +{BalanceStatic.AttributePointsPerLevel} очков атрибутов");
            await SendToClient(connection, GameMessage.SystemChat($"Уровень повышен! Вы теперь уровень {player.Level}! +{BalanceStatic.AttributePointsPerLevel} очков атрибутов. HP восстановлены."));
        }

        await SendQuestLog(connection, player);
        await SendInventoryAndStatus(connection, player);
    }
}

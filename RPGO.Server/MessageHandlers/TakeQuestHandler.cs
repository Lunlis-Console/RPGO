using RPGGame.Server.Network;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;
using System.Text.Json;

namespace RPGGame.Server.MessageHandlers;

public class TakeQuestHandler : BaseHandler
{
    public TakeQuestHandler(GameWorld world, INetworkHub hub) : base(world, hub) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;
        if (message.Data is not JsonElement takeEl) return;

        string? questId = takeEl.ValueKind == JsonValueKind.String
            ? takeEl.GetString()
            : takeEl.TryGetProperty("QuestId", out var tqProp) ? tqProp.GetString() : null;

        if (!Program.Services.Quests.IsAtBoard(player.X, player.Y))
        {
            await SendError(connection, ErrorCodes.NotAtBoard, "Доска заданий далеко. Подойдите к ней, чтобы взять задание.");
            return;
        }

        if (questId == null)
        {
            await SendError(connection, ErrorCodes.QuestNotSpecified, "Задание не указано.");
            return;
        }

        if (player.ActiveQuests.Any(q => q.QuestId == questId))
        {
            await SendError(connection, ErrorCodes.QuestAlreadyTaken, "Вы уже взяли это задание.");
            return;
        }

        var def = Program.Services.Quests.FindQuest(questId);
        if (def == null)
        {
            await SendError(connection, ErrorCodes.QuestNotFound, "Такого задания не существует.");
            return;
        }

        int currentProgress = 0;
        if (def.Type == "collect" && !string.IsNullOrEmpty(def.TargetItemId))
            currentProgress = player.Inventory.Count(i => i.Id == def.TargetItemId);
        bool alreadyCompleted = currentProgress >= def.Target;
        player.ActiveQuests.Add(new QuestProgress { QuestId = def.Id, Current = currentProgress, Completed = alreadyCompleted });
        Log.Info($"{player.Name} взял задание: {def.Title} (прогресс: {currentProgress}/{def.Target})");

        if (alreadyCompleted)
        {
            await SendToClient(connection, new GameMessage
            {
                Type = "chat",
                Data = new { Name = "Система", Text = $"Задание принято: {def.Title} — уже выполнено! Сдайте его на доске." }
            });
        }
        else
        {
            await SendToClient(connection, new GameMessage
            {
                Type = "chat",
                Data = new { Name = "Система", Text = $"Задание принято: {def.Title} — {def.Description} (прогресс: {currentProgress}/{def.Target})" }
            });
        }
        await SendQuestLog(connection, player);
    }
}

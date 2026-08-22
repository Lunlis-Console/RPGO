using LostAndDivine.Server.Network;
using LostAndDivine.Server.Services;
using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;
using System.Text.Json;

namespace LostAndDivine.Server.MessageHandlers;

public class TakeQuestHandler : BaseHandler
{
    public TakeQuestHandler(GameServices svc) : base(svc) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;
        if (message.Data is not JsonElement takeEl) return;

        string? questId = takeEl.ValueKind == JsonValueKind.String
            ? takeEl.GetString()
            : takeEl.TryGetProperty("QuestId", out var tqProp) ? tqProp.GetString() : null;

        if (!Svc.Quests.IsAtBoard(player.X, player.Y))
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

        var def = Svc.Quests.FindQuest(questId);
        if (def == null)
        {
            await SendError(connection, ErrorCodes.QuestNotFound, "Такого задания не существует.");
            return;
        }

        if (def.GiverNpcId != Svc.Quests.BoardNpcId)
        {
            await SendError(connection, ErrorCodes.QuestNotAvailable,
                "Это задание выдаётся у NPC, а не на доске заданий.");
            return;
        }

        if (!Svc.Quests.CanTakeQuest(player, def))
        {
            if (player.Level < def.MinLevel)
            {
                await SendError(connection, ErrorCodes.QuestNotAvailable, $"Это задание требует {def.MinLevel} уровня.");
                return;
            }
            if (!string.IsNullOrEmpty(def.PrerequisiteQuestId) &&
                !player.CompletedQuestIds.Contains(def.PrerequisiteQuestId))
            {
                await SendError(connection, ErrorCodes.QuestNotAvailable, "Сначала выполните предыдущее задание цепочки.");
                return;
            }
            await SendError(connection, ErrorCodes.QuestNotAvailable, "Это задание сейчас недоступно.");
            return;
        }

        Svc.Quests.TakeQuest(player, def);
        var prog = player.ActiveQuests.FirstOrDefault(q => q.QuestId == def.Id);
        int currentProgress = prog?.Current ?? 0;
        bool alreadyCompleted = prog?.Completed ?? false;
        Log.Info($"{player.Name} взял задание: {def.Title} (прогресс: {currentProgress}/{def.Target})");

        if (alreadyCompleted)
        {
            await SendToClient(connection, new GameMessage
            {
                Type = GameMessageType.Chat,
                Data = new { Name = "Система", Text = $"Задание принято: {def.Title} — уже выполнено! Сдайте его на доске." }
            });
        }
        else
        {
            await SendToClient(connection, new GameMessage
            {
                Type = GameMessageType.Chat,
                Data = new { Name = "Система", Text = $"Задание принято: {def.Title} — {def.Description} (прогресс: {currentProgress}/{def.Target})" }
            });
        }
        await SendQuestLog(connection, player);
    }
}

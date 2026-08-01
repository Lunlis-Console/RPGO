using RPGGame.Server.Network;
using RPGGame.Server.Services;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;
using System.Text.Json;

namespace RPGGame.Server.MessageHandlers;

public class CompleteQuestHandler : BaseHandler
{
    public CompleteQuestHandler(GameServices svc) : base(svc) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;
        if (message.Data is not JsonElement compEl) return;

        string? questId = compEl.ValueKind == JsonValueKind.String
            ? compEl.GetString()
            : compEl.TryGetProperty("QuestId", out var cqProp) ? cqProp.GetString() : null;

        if (!Svc.Quests.IsAtBoard(player.X, player.Y))
        {
            await SendError(connection, ErrorCodes.NotAtBoard, "Вернитесь к доске заданий, чтобы сдать задание.");
            return;
        }

        if (questId == null)
        {
            await SendError(connection, ErrorCodes.QuestNotSpecified, "Задание не указано.");
            return;
        }

        var result = Svc.Quests.CompleteQuest(player, questId);
        if (!result.Success)
        {
            string code = result.ErrorKind == 1 ? ErrorCodes.QuestNotActive : ErrorCodes.QuestNotCompleted;
            await SendError(connection, code, result.Message);
            return;
        }

        await SendToClient(connection, new GameMessage
        {
            Type = "chat",
            Data = new { Name = "Система", Text = result.Message }
        });

        if (result.LeveledUp)
        {
            string skillMsg = player.Level % 2 == 0 ? $" +1 очко навыков." : "";
            Log.Info($"{player.Name} повысил уровень до {player.Level}! +{BalanceStatic.AttributePointsPerLevel} очков атрибутов{skillMsg}");
            await SendToClient(connection, GameMessage.SystemChat($"Уровень повышен! Вы теперь уровень {player.Level}! +{BalanceStatic.AttributePointsPerLevel} очков атрибутов.{skillMsg} HP восстановлены."));
        }

        await SendQuestLog(connection, player);
        await SendInventoryAndStatus(connection, player);
    }
}

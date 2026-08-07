using LostAndDivine.Server.Network;
using LostAndDivine.Server.Services;
using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;
using System.Text.Json;

namespace LostAndDivine.Server.MessageHandlers;

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
            await SendError(connection, ErrorCodes.NotAtBoard, "��������� � ����� �������, ����� ����� �������.");
            return;
        }

        if (questId == null)
        {
            await SendError(connection, ErrorCodes.QuestNotSpecified, "������� �� �������.");
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
            Data = new { Name = "�������", Text = result.Message }
        });

        if (result.LeveledUp)
        {
            string skillMsg = player.Level % 2 == 0 ? $" +1 ���� �������." : "";
            Log.Info($"{player.Name} ������� ������� �� {player.Level}! +{BalanceStatic.AttributePointsPerLevel} ����� ���������{skillMsg}");
            await SendToClient(connection, GameMessage.SystemChat($"������� �������! �� ������ ������� {player.Level}! +{BalanceStatic.AttributePointsPerLevel} ����� ���������.{skillMsg} HP �������������."));
        }

        await SendQuestLog(connection, player);
        await SendInventoryAndStatus(connection, player);
        Hub.MarkZoneDirty(player.CurrentZoneId);
        await BroadcastMapAsync();
    }
}

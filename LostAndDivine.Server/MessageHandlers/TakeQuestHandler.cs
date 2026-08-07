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
            await SendError(connection, ErrorCodes.NotAtBoard, "����� ������� ������. ��������� � ���, ����� ����� �������.");
            return;
        }

        if (questId == null)
        {
            await SendError(connection, ErrorCodes.QuestNotSpecified, "������� �� �������.");
            return;
        }

        if (player.ActiveQuests.Any(q => q.QuestId == questId))
        {
            await SendError(connection, ErrorCodes.QuestAlreadyTaken, "�� ��� ����� ��� �������.");
            return;
        }

        var def = Svc.Quests.FindQuest(questId);
        if (def == null)
        {
            await SendError(connection, ErrorCodes.QuestNotFound, "������ ������� �� ����������.");
            return;
        }

        if (!Svc.Quests.CanTakeQuest(player, def))
        {
            if (player.Level < def.MinLevel)
            {
                await SendError(connection, ErrorCodes.QuestNotAvailable, $"��� ������� ������� {def.MinLevel} ������.");
                return;
            }
            if (!string.IsNullOrEmpty(def.PrerequisiteQuestId) &&
                !player.CompletedQuestIds.Contains(def.PrerequisiteQuestId))
            {
                await SendError(connection, ErrorCodes.QuestNotAvailable, "������� ��������� ���������� ������� �������.");
                return;
            }
            await SendError(connection, ErrorCodes.QuestNotAvailable, "��� ������� ������ ����������.");
            return;
        }

        Svc.Quests.TakeQuest(player, def);
        var prog = player.ActiveQuests.FirstOrDefault(q => q.QuestId == def.Id);
        int currentProgress = prog?.Current ?? 0;
        bool alreadyCompleted = prog?.Completed ?? false;
        Log.Info($"{player.Name} ���� �������: {def.Title} (��������: {currentProgress}/{def.Target})");

        if (alreadyCompleted)
        {
            await SendToClient(connection, new GameMessage
            {
                Type = "chat",
                Data = new { Name = "�������", Text = $"������� �������: {def.Title} � ��� ���������! ������ ��� �� �����." }
            });
        }
        else
        {
            await SendToClient(connection, new GameMessage
            {
                Type = "chat",
                Data = new { Name = "�������", Text = $"������� �������: {def.Title} � {def.Description} (��������: {currentProgress}/{def.Target})" }
            });
        }
        await SendQuestLog(connection, player);
    }
}

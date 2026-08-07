using LostAndDivine.Server.Network;
using LostAndDivine.Server.Services;
using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;
using System.Text.Json;

namespace LostAndDivine.Server.MessageHandlers;

public class AbandonQuestHandler : BaseHandler
{
    public AbandonQuestHandler(GameServices svc) : base(svc) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;
        if (message.Data is not JsonElement el) return;

        string? questId = el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : el.TryGetProperty("QuestId", out var qProp) ? qProp.GetString() : null;

        if (questId == null)
        {
            await SendError(connection, ErrorCodes.QuestNotSpecified, "������� �� �������.");
            return;
        }

        var prog = player.ActiveQuests.FirstOrDefault(q => q.QuestId == questId);
        if (prog == null)
        {
            await SendError(connection, ErrorCodes.QuestNotActive, "� ��� ��� ����� �������.");
            return;
        }

        if (prog.Completed)
        {
            await SendError(connection, ErrorCodes.QuestNotActive, "������� ������� ������ ��������.");
            return;
        }

        var def = Svc.Quests.FindQuest(questId);
        player.ActiveQuests.Remove(prog);
        Log.Info($"{player.Name} ��������� �� ������� {def?.Title ?? questId}");
        await SendToClient(connection, new GameMessage
        {
            Type = "chat",
            Data = new { Name = "�������", Text = $"�� ���������� �� �������: {def?.Title ?? questId}." }
        });

        await SendQuestLog(connection, player);
    }
}

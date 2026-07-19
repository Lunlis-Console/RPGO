using RPGGame.Server.Network;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;
using System.Text.Json;

namespace RPGGame.Server.MessageHandlers;

public class AbandonQuestHandler : BaseHandler
{
    public AbandonQuestHandler(GameWorld world, INetworkHub hub) : base(world, hub) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;
        if (message.Data is not JsonElement el) return;

        string? questId = el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : el.TryGetProperty("QuestId", out var qProp) ? qProp.GetString() : null;

        if (questId == null)
        {
            await SendError(connection, ErrorCodes.QuestNotSpecified, "Задание не указано.");
            return;
        }

        var prog = player.ActiveQuests.FirstOrDefault(q => q.QuestId == questId);
        if (prog == null)
        {
            await SendError(connection, ErrorCodes.QuestNotActive, "У вас нет этого задания.");
            return;
        }

        if (prog.Completed)
        {
            await SendError(connection, ErrorCodes.QuestNotActive, "Сданное задание нельзя отменить.");
            return;
        }

        var def = QuestManager.FindQuest(questId);
        player.ActiveQuests.Remove(prog);
        Log.Info($"{player.Name} отказался от задания {def?.Title ?? questId}");
        await SendToClient(connection, new GameMessage
        {
            Type = "chat",
            Data = new { Name = "Система", Text = $"Вы отказались от задания: {def?.Title ?? questId}." }
        });

        await SendQuestLog(connection, player);
    }
}

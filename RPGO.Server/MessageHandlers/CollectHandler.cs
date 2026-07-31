using RPGGame.Server.Network;
using RPGGame.Server.Services;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;

namespace RPGGame.Server.MessageHandlers;

public class CollectHandler : BaseHandler
{
    public CollectHandler(GameServices svc) : base(svc) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;

        var lootItem = Svc.Collectibles.TryCollect(player.X, player.Y, player.CurrentZoneId);
        if (lootItem == null)
        {
            await SendError(connection, ErrorCodes.NothingToCollect, "Здесь нечего собирать.");
            return;
        }

        InventoryHelper.AddItem(player, lootItem);
        await SendToClient(connection, new GameMessage
        {
            Type = "chat",
            Data = new { Name = "Система", Text = $"[Сбор] Вы собрали: {lootItem.Name}!" }
        });

        var collectResults = Svc.Quests.IncrementCollectProgress(player, lootItem.Id);
        foreach (var (title, current, target, completed) in collectResults)
        {
            string msg = completed
                ? $"[Задание] {title}: {current}/{target} — задание выполнено! Вернитесь на доску заданий, чтобы сдать."
                : $"[Задание] {title}: {current}/{target}";
            await SendToClient(connection, new GameMessage
            {
                Type = "chat",
                Data = new { Name = "Система", Text = msg }
            });
        }

        await SendQuestLog(connection, player);
        await BroadcastMapAsync();
    }
}

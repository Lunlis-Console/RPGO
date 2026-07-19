using RPGGame.Server.Network;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;

namespace RPGGame.Server.MessageHandlers;

public class CollectHandler : BaseHandler
{
    public CollectHandler(GameWorld world, INetworkHub hub) : base(world, hub) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;

        var lootItem = CollectibleManager.TryCollect(player.X, player.Y);
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

        var collectResults = QuestManager.IncrementCollectProgress(player, lootItem.Id);
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

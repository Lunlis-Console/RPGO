using LostAndDivine.Server.Network;
using LostAndDivine.Server.Services;
using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;

namespace LostAndDivine.Server.MessageHandlers;

public class CollectHandler : BaseHandler
{
    public CollectHandler(GameServices svc) : base(svc) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;

        var lootItem = Svc.Collectibles.TryCollect(player.X, player.Y, player.CurrentZoneId);
        if (lootItem == null)
        {
            await SendError(connection, ErrorCodes.NothingToCollect, "����� ������ ��������.");
            return;
        }

        InventoryHelper.AddItem(player, lootItem);
        await SendToClient(connection, new GameMessage
        {
            Type = "chat",
            Data = new { Name = "�������", Text = $"[����] �� �������: {lootItem.Name}!" }
        });

        var collectResults = Svc.Quests.IncrementCollectProgress(player, lootItem.Id);
        foreach (var (title, current, target, completed) in collectResults)
        {
            string msg = completed
                ? $"[�������] {title}: {current}/{target} � ������� ���������! ��������� �� ����� �������, ����� �����."
                : $"[�������] {title}: {current}/{target}";
            await SendToClient(connection, new GameMessage
            {
                Type = "chat",
                Data = new { Name = "�������", Text = msg }
            });
        }

        await SendQuestLog(connection, player);
        await BroadcastMapAsync();
    }
}

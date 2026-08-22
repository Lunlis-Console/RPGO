using LostAndDivine.Server.Network;
using LostAndDivine.Server.Services;
using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;

namespace LostAndDivine.Server.MessageHandlers;

public class UnequipAllHandler : BaseHandler
{
    public UnequipAllHandler(GameServices svc) : base(svc) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;
        if (player.IsTrading) { await SendError(connection, ErrorCodes.InvalidRequest, "Нельзя снять во время обмена!"); return; }

        // Один и тот же предмет может занимать два слота (напр. перчатки) —
        // снимаем его один раз и возвращаем в инвентарь одной записью.
        var worn = EquipmentSlots.All
            .Select(s => player.Equipment[s.Id])
            .Where(i => i != null)
            .GroupBy(i => i!.Id)
            .Select(g => g.First()!)
            .ToList();

        if (worn.Count == 0)
        {
            await SendToClient(connection, new GameMessage
            {
                Type = GameMessageType.Chat,
                Data = new { Name = "Система", Text = "Нет надетого снаряжения." }
            });
            return;
        }

        foreach (var s in EquipmentSlots.All)
            player.Equipment[s.Id] = null;
        foreach (var item in worn)
            InventoryHelper.AddItem(player, item);

        Log.Debug($"{player.Name} снял всё снаряжение ({worn.Count} предметов)");
        await SendToClient(connection, new GameMessage
        {
            Type = GameMessageType.Chat,
            Data = new { Name = "Система", Text = $"Вы сняли всё снаряжение ({worn.Count} шт.)" }
        });
        await SendInventoryAndStatus(connection, player, fromUnequip: true);
    }
}

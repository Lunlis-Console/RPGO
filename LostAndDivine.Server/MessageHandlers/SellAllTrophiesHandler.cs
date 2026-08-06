using LostAndDivine.Server.Network;
using LostAndDivine.Server.Services;
using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;

namespace LostAndDivine.Server.MessageHandlers;

public class SellAllTrophiesHandler : BaseHandler
{
    public SellAllTrophiesHandler(GameServices svc) : base(svc) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;
        if (player.IsTrading) { await SendError(connection, ErrorCodes.InvalidRequest, "Нельзя продавать во время обмена!"); return; }

        var trophies = player.Inventory.Where(i => i.Type == "trophy").ToList();
        if (trophies.Count == 0)
        {
            await SendError(connection, ErrorCodes.InvalidRequest, "У вас нет трофеев для продажи!");
            return;
        }

        int totalQty = trophies.Sum(i => i.Quantity);
        int totalGain = 0;

        // Группируем трофеи по типу для корректного buyback (каждый тип — отдельная запись)
        var byType = trophies.GroupBy(i => new {
            i.Name, i.Type, i.Value, i.Description,
            i.BonusPhysAttack, i.BonusDefense, i.MaxHealthBonus, i.HealAmount, i.RestoreMana
        });

        foreach (var group in byType)
        {
            int groupQty = group.Sum(i => i.Quantity);
            int sellPrice = Balance.SellPrice(group.First().Value);
            int groupGain = sellPrice * groupQty;
            player.Gold += groupGain;
            totalGain += groupGain;

            // Удаляем из инвентаря
            foreach (var item in group)
                player.Inventory.Remove(item);

            // Buyback: одна запись на группу
            var first = group.First();
            var buybackCopy = first.Clone();
            buybackCopy.Id = Guid.NewGuid().ToString();
            buybackCopy.Quantity = groupQty;
            player.BuybackItems.Add(buybackCopy);
        }

        Log.Info($"{player.Name} продал все трофеи x{totalQty} за {totalGain} золота");
        await SendToClient(connection, new GameMessage
        {
            Type = "chat",
            Data = new { Name = "Система", Text = $"Вы продали все трофеи x{totalQty} за {totalGain} золота" }
        });
        await SendToClient(connection, new GameMessage
        {
            Type = "shop_update",
            Data = new
            {
                PlayerGold = player.Gold,
                Buyback = player.BuybackItems.Select(b => new
                {
                    b.Id, b.Name, b.Type,
                    Value = Balance.BuybackPrice(b.Value),
                    OriginalValue = b.Value,
                    b.MaxHealthBonus, b.HealAmount, b.RestoreMana, b.Description,
                    b.Quantity, IsBuyback = true
                }).ToList()
            }
        });
        await SendInventoryAndStatus(connection, player);
    }
}

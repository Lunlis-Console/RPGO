using RPGGame.Server.Network;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;

namespace RPGGame.Server.MessageHandlers;

public class SellAllTrophiesHandler : BaseHandler
{
    public SellAllTrophiesHandler(GameWorld world, INetworkHub hub) : base(world, hub) { }

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
        foreach (var item in trophies)
        {
            int sellPrice = Balance.SellPrice(item.Value);
            player.Gold += sellPrice * item.Quantity;
            totalGain += sellPrice * item.Quantity;
            player.Inventory.Remove(item);
        }

        var first = trophies.First();
        var buybackCopy = first.Clone();
        buybackCopy.Id = Guid.NewGuid().ToString();
        buybackCopy.Quantity = totalQty;
        player.BuybackItems.Add(buybackCopy);

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
                    b.MaxHealthBonus, b.HealAmount, b.Description,
                    IsBuyback = true
                }).ToList()
            }
        });
        await SendInventoryAndStatus(connection, player);
    }
}

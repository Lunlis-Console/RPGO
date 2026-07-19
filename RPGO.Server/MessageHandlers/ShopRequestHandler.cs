using RPGGame.Server.Network;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;

namespace RPGGame.Server.MessageHandlers;

public class ShopRequestHandler : BaseHandler
{
    public ShopRequestHandler(GameWorld world, INetworkHub hub) : base(world, hub) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;

        int discountPct = Balance.ShopDiscountPct(player.Cunning);
        await SendToClient(connection, new GameMessage
        {
            Type = "shop_response",
            Data = new
            {
                MerchantX = MerchantManager.MerchantX,
                MerchantY = MerchantManager.MerchantY,
                MerchantName = "Торговец",
                Discount = discountPct,
                Items = MerchantManager.ShopItems.Select(i => new
                {
                    i.Id, i.Name, i.Type,
                    Value = Balance.BuyPrice(i.Value, discountPct),
                    OriginalValue = i.Value,
                    i.Attack, i.Defense, i.MaxHealthBonus, i.HealAmount, i.Description,
                    i.Stock,
                    IsBuyback = false
                }).ToList(),
                Buyback = player.BuybackItems.Select(i => new
                {
                    i.Id, i.Name, i.Type,
                    Value = Balance.BuybackPrice(i.Value),
                    OriginalValue = i.Value,
                    i.Attack, i.Defense, i.MaxHealthBonus, i.HealAmount, i.Description,
                    IsBuyback = true
                }).ToList(),
                PlayerGold = player.Gold
            }
        });
    }
}

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

        await SendToClient(connection, new GameMessage
        {
            Type = "shop_response",
            Data = new
            {
                MerchantX = Program.Services.Merchant.MerchantX,
                MerchantY = Program.Services.Merchant.MerchantY,
                MerchantName = "Торговец",
                Discount = 0,
                Items = Program.Services.Merchant.ShopItems.Select(i => new
                {
                    i.Id, i.Name, i.Type, i.WeaponSubtype,
                    Value = Balance.BuyPrice(i.Value),
                    OriginalValue = i.Value,
                    i.MaxHealthBonus, i.HealAmount, i.RestoreMana, i.Description,
                    i.Stock,
                    IsBuyback = false
                }).ToList(),
                Buyback = player.BuybackItems.Select(i => new
                {
                    i.Id, i.Name, i.Type, i.WeaponSubtype,
                    Value = Balance.BuybackPrice(i.Value),
                    OriginalValue = i.Value,
                    i.MaxHealthBonus, i.HealAmount, i.RestoreMana, i.Description,
                    i.Quantity, IsBuyback = true
                }).ToList(),
                PlayerGold = player.Gold
            }
        });
    }
}

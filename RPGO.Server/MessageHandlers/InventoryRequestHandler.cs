using RPGGame.Server.Network;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;

namespace RPGGame.Server.MessageHandlers;

public class InventoryRequestHandler : BaseHandler
{
    public InventoryRequestHandler(GameWorld world, INetworkHub hub) : base(world, hub) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;

        await SendToClient(connection, new GameMessage
        {
            Type = "inventory_response",
            Data = new
            {
                Items = player.Inventory,
                Gold = player.Gold,
                Equipment = new
                {
                    Weapon = player.Equipment.Weapon,
                    Armor = player.Equipment.Armor,
                    Accessory = player.Equipment.Accessory
                },
                BonusAttack = player.Equipment.GetBonusAttack(),
                BonusDefense = player.Equipment.GetBonusDefense(),
                BonusMaxHealth = player.Equipment.GetBonusMaxHealth()
            }
        });
    }
}

using RPGGame.Server.Network;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;
using RPGGame.Shared.Commands;
using System.Text.Json;

namespace RPGGame.Server.MessageHandlers;

public class TradeOfferHandler : BaseHandler
{
    public TradeOfferHandler(GameWorld world, INetworkHub hub) : base(world, hub) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;

        var session = TradeManager.GetSession(player.Id);
        if (session == null)
        {
            await SendError(connection, ErrorCodes.InvalidRequest, "Нет активного обмена.");
            return;
        }

        if (session.BothConfirmed)
        {
            await SendError(connection, ErrorCodes.InvalidRequest, "Обмен уже подтверждён.");
            return;
        }

        session.ResetConfirms();

        if (message.Data is not JsonElement el) return;

        var myItemIds = new List<string>();
        if (el.TryGetProperty("ItemIds", out var itemsArr) && itemsArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var itemEl in itemsArr.EnumerateArray())
            {
                if (itemEl.ValueKind == JsonValueKind.String)
                {
                    string? id = itemEl.GetString();
                    if (id != null) myItemIds.Add(id);
                }
            }
        }

        int myGold = 0;
        if (el.TryGetProperty("Gold", out var goldProp))
            myGold = Math.Max(0, goldProp.GetInt32());

        var other = session.GetOther(player);
        if (other == null) return;

        if (!ValidateItems(player, myItemIds))
        {
            await SendError(connection, ErrorCodes.InvalidRequest, "Некоторые предметы недоступны.");
            return;
        }

        myGold = Math.Min(myGold, player.Gold);

        bool isInitiator = player.Id == session.Initiator.Id;
        if (isInitiator)
        {
            session.InitiatorItemIds.Clear();
            session.InitiatorItemIds.AddRange(myItemIds);
            session.InitiatorGold = myGold;
        }
        else
        {
            session.PartnerItemIds.Clear();
            session.PartnerItemIds.AddRange(myItemIds);
            session.PartnerGold = myGold;
        }

        var otherConn = World.FindClientByPlayer(other);
        var myConn = World.FindClientByPlayer(player);

        var myOffer = BuildOfferSummary(player, myItemIds, myGold);

        if (otherConn != null)
        {
            await SendToClient(otherConn, new GameMessage
            {
                Type = "trade_offer_update",
                Data = new
                {
                    IsFromMe = false,
                    Offer = myOffer
                }
            });
        }

        if (myConn != null)
        {
            await SendToClient(myConn, new GameMessage
            {
                Type = "trade_offer_update",
                Data = new
                {
                    IsFromMe = true,
                    Offer = myOffer
                }
            });
        }

        Log.Debug($"Трейд предложение: {player.Name} предложил {myItemIds.Count} предметов, {myGold} золота");
    }

    private static bool ValidateItems(Player player, List<string> itemIds)
    {
        if (itemIds.Count != itemIds.Distinct().Count())
            return false;

        var owned = new HashSet<string>(player.Inventory.Select(i => i.Id));
        foreach (var id in itemIds)
        {
            if (string.IsNullOrEmpty(id) || !owned.Contains(id))
                return false;
        }
        return true;
    }

    private static object BuildOfferSummary(Player player, List<string> itemIds, int gold)
    {
        var items = itemIds
            .Select(id => player.Inventory.FirstOrDefault(i => i.Id == id))
            .Where(i => i != null)
            .Select(i => new
            {
                i!.Id, i.Name, i.Type, i.Value, i.Description,
                i.Attack, i.Defense, i.MaxHealthBonus, i.HealAmount, i.MaxStack
            })
            .ToList();

        return new
        {
            Items = items,
            Gold = gold
        };
    }
}

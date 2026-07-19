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

        var myEntries = new List<TradeOfferEntry>();
        if (el.TryGetProperty("Entries", out var entriesArr) && entriesArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var entryEl in entriesArr.EnumerateArray())
            {
                if (entryEl.ValueKind != JsonValueKind.Object) continue;
                string? tid = entryEl.TryGetProperty("TemplateId", out var tEl) ? tEl.GetString() : null;
                int qty = entryEl.TryGetProperty("Quantity", out var qEl) && qEl.ValueKind == JsonValueKind.Number
                    ? Math.Max(1, qEl.GetInt32()) : 1;
                if (!string.IsNullOrEmpty(tid))
                    myEntries.Add(new TradeOfferEntry { TemplateId = tid, Quantity = qty });
            }
        }

        int myGold = 0;
        if (el.TryGetProperty("Gold", out var goldProp))
            myGold = Math.Max(0, goldProp.GetInt32());

        var other = session.GetOther(player);
        if (other == null) return;

        if (!ValidateOffer(player, myEntries, myGold))
        {
            await SendError(connection, ErrorCodes.InvalidRequest, "Некоторые предметы недоступны или количество превышено.");
            return;
        }

        myGold = Math.Min(myGold, player.Gold);

        bool isInitiator = player.Id == session.Initiator.Id;
        var target = isInitiator ? session.InitiatorItemIds : session.PartnerItemIds;
        target.Clear();
        target.AddRange(myEntries);
        if (isInitiator) session.InitiatorGold = myGold;
        else session.PartnerGold = myGold;

        var otherConn = World.FindClientByPlayer(other);
        var myConn = World.FindClientByPlayer(player);

        var myOffer = BuildOfferSummary(player, myEntries, myGold);

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

        int totalItems = myEntries.Sum(e => e.Quantity);
        Log.Debug($"Трейд предложение: {player.Name} предложил {totalItems} предметов ({myEntries.Count} типов), {myGold} золота");
    }

    private static bool ValidateOffer(Player player, List<TradeOfferEntry> entries, int gold)
    {
        if (gold > player.Gold) return false;
        foreach (var e in entries)
        {
            int available = player.Inventory
                .Where(i => i.TemplateId == e.TemplateId)
                .Sum(i => i.Quantity);
            if (available < e.Quantity)
                return false;
        }
        return true;
    }

    private static object BuildOfferSummary(Player player, List<TradeOfferEntry> entries, int gold)
    {
        var items = entries
            .Select(e => player.Inventory.FirstOrDefault(i => i.TemplateId == e.TemplateId))
            .Where(i => i != null)
            .Select(i => new
            {
                i!.Id, i.Name, i.Type, i.Value, i.Description,
                i.Attack, i.Defense, i.MaxHealthBonus, i.HealAmount, i.MaxStack,
                Quantity = entries.First(x => x.TemplateId == i.TemplateId).Quantity
            })
            .ToList();

        return new
        {
            Items = items,
            Gold = gold
        };
    }
}

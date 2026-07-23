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

        var session = Program.Services.Trade.GetSession(player.Id);
        if (session == null)
        {
            Log.Warn($"TRADE OFFER: нет сессии у {player.Name} (id={player.Id})");
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
                string? iid = entryEl.TryGetProperty("ItemId", out var iEl) ? iEl.GetString() : null;
                int qty = entryEl.TryGetProperty("Quantity", out var qEl) && qEl.ValueKind == JsonValueKind.Number
                    ? Math.Max(1, qEl.GetInt32()) : 1;
                if (!string.IsNullOrEmpty(iid))
                    myEntries.Add(new TradeOfferEntry { ItemId = iid, Quantity = qty });
            }
        }

        int myGold = 0;
        if (el.TryGetProperty("Gold", out var goldProp))
            myGold = Math.Max(0, goldProp.GetInt32());

        var other = session.GetOther(player);
        if (other == null) return;

        Log.Debug($"TRADE OFFER от {player.Name}: " +
            string.Join(", ", myEntries.Select(e => $"{e.ItemId}x{e.Quantity}")) +
            $" | золото={myGold}");

        if (!ValidateOffer(player, myEntries, myGold))
        {
            var details = string.Join("; ", myEntries.Select(e =>
                $"{e.ItemId}: нужно {e.Quantity}, есть {player.Inventory.FirstOrDefault(i => i.Id == e.ItemId)?.Quantity ?? 0}"));
            Log.Warn($"TRADE OFFER НЕ прошёл валидацию у {player.Name}: {details}");
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
            var item = player.Inventory.FirstOrDefault(i => i.Id == e.ItemId);
            if (item == null || item.Quantity < e.Quantity)
                return false;
        }
        return true;
    }

    private static object BuildOfferSummary(Player player, List<TradeOfferEntry> entries, int gold)
    {
        var items = entries
            .Select(e => player.Inventory.FirstOrDefault(i => i.Id == e.ItemId))
            .Where(i => i != null)
            .Select(i => new
            {
                i!.Id, i.TemplateId, i.Name, i.Type, i.Value, i.Description,
                i.MaxHealthBonus, i.HealAmount, i.MaxStack,
                Quantity = entries.First(x => x.ItemId == i.Id).Quantity
            })
            .ToList();

        return new
        {
            Items = items,
            Gold = gold
        };
    }
}

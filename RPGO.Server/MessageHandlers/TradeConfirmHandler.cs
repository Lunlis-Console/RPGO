using RPGGame.Server.Network;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;
using RPGGame.Shared.Commands;
using System.Text.Json;

namespace RPGGame.Server.MessageHandlers;

public class TradeConfirmHandler : BaseHandler
{
    public TradeConfirmHandler(GameWorld world, INetworkHub hub) : base(world, hub) { }

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

        bool confirmed = true;
        if (message.Data is JsonElement el && el.TryGetProperty("Confirmed", out var cP))
            confirmed = cP.GetBoolean();

        bool isInitiator = player.Id == session.Initiator.Id;
        if (isInitiator) session.InitiatorConfirmed = confirmed;
        else session.PartnerConfirmed = confirmed;

        var other = session.GetOther(player);
        if (other == null) return;

        var otherConn = World.FindClientByPlayer(other);
        var myConn = World.FindClientByPlayer(player);

        var myUpdate = new GameMessage
        {
            Type = "trade_confirm_update",
            Data = new
            {
                YouConfirmed = confirmed,
                OtherConfirmed = isInitiator ? session.PartnerConfirmed : session.InitiatorConfirmed
            }
        };

        var otherUpdate = new GameMessage
        {
            Type = "trade_confirm_update",
            Data = new
            {
                YouConfirmed = isInitiator ? session.PartnerConfirmed : session.InitiatorConfirmed,
                OtherConfirmed = confirmed
            }
        };

        if (myConn != null) await SendToClient(myConn, myUpdate);
        if (otherConn != null) await SendToClient(otherConn, otherUpdate);

        if (session.BothConfirmed)
        {
            await ExecuteSwap(session);
        }
    }

    private async Task ExecuteSwap(TradeSession session)
    {
        var initiator = session.Initiator;
        var partner = session.Partner;

        if (!ValidateFinalOffer(initiator, session.InitiatorItemIds, session.InitiatorGold) ||
            !ValidateFinalOffer(partner, session.PartnerItemIds, session.PartnerGold))
        {
            await NotifyError(session, "Предметы изменились. Обмен отменён.");
            TradeManager.CancelSession(session, "validation failed");
            return;
        }

        var initiatorItems = session.InitiatorItemIds
            .Select(id => initiator.Inventory.FirstOrDefault(i => i.Id == id))
            .Where(i => i != null)
            .Select(i => i!)
            .ToList();

        var partnerItems = session.PartnerItemIds
            .Select(id => partner.Inventory.FirstOrDefault(i => i.Id == id))
            .Where(i => i != null)
            .Select(i => i!)
            .ToList();

        int initiatorGold = Math.Min(session.InitiatorGold, initiator.Gold);
        int partnerGold = Math.Min(session.PartnerGold, partner.Gold);

        foreach (var item in initiatorItems)
        {
            initiator.Inventory.Remove(item);
            partner.Inventory.Add(item);
        }

        foreach (var item in partnerItems)
        {
            partner.Inventory.Remove(item);
            initiator.Inventory.Add(item);
        }

        initiator.Gold -= initiatorGold;
        partner.Gold -= partnerGold;
        partner.Gold += initiatorGold;
        initiator.Gold += partnerGold;

        initiator.IsTrading = false;
        partner.IsTrading = false;

        var initiatorConn = World.FindClientByPlayer(initiator);
        var partnerConn = World.FindClientByPlayer(partner);

        var completeMsg = new GameMessage
        {
            Type = "trade_complete",
            Data = new { Success = true, Message = "Обмен успешно завершён!" }
        };

        if (initiatorConn != null)
        {
            await SendToClient(initiatorConn, completeMsg);
            await SendInventoryAndStatus(initiatorConn, initiator);
        }

        if (partnerConn != null)
        {
            await SendToClient(partnerConn, completeMsg);
            await SendInventoryAndStatus(partnerConn, partner);
        }

        TradeManager.RemoveSession(session);

        Log.Info($"ТРЕЙД ВЫПОЛНЕН: {initiator.Name} ↔ {partner.Name} | " +
                 $"{initiator.Name} отдал {initiatorItems.Count} предметов + {initiatorGold} золота; " +
                 $"{partner.Name} отдал {partnerItems.Count} предметов + {partnerGold} золота");
    }

    private static bool ValidateFinalOffer(Player player, List<string> itemIds, int gold)
    {
        if (gold > player.Gold) return false;
        foreach (var id in itemIds)
        {
            if (player.Inventory.FirstOrDefault(i => i.Id == id) == null)
                return false;
        }
        return true;
    }

    private async Task NotifyError(TradeSession session, string msg)
    {
        var initiatorConn = World.FindClientByPlayer(session.Initiator);
        var partnerConn = World.FindClientByPlayer(session.Partner);

        var errorMsg = new GameMessage
        {
            Type = "trade_complete",
            Data = new { Success = false, Message = msg }
        };

        if (initiatorConn != null) await SendToClient(initiatorConn, errorMsg);
        if (partnerConn != null) await SendToClient(partnerConn, errorMsg);
    }
}

using LostAndDivine.Server.Network;
using LostAndDivine.Server.Services;
using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;
using LostAndDivine.Shared.Commands;
using System.Text.Json;

namespace LostAndDivine.Server.MessageHandlers;

public class TradeConfirmHandler : BaseHandler
{
    public TradeConfirmHandler(GameServices svc) : base(svc) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;

        var session = Svc.Trade.GetSession(player.Id);
        if (session == null)
        {
            Log.Warn($"TRADE CONFIRM: ��� ������ � {player.Name} (id={player.Id})");
            await SendError(connection, ErrorCodes.InvalidRequest, "��� ��������� ������.");
            return;
        }

        if (session.BothConfirmed)
        {
            await SendError(connection, ErrorCodes.InvalidRequest, "����� ��� ����������.");
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
            await NotifyError(session, "�������� ����������. ����� ������.");
            Svc.Trade.CancelSession(session, "validation failed");
            return;
        }

        int initiatorGold = Math.Min(session.InitiatorGold, initiator.Gold);
        int partnerGold = Math.Min(session.PartnerGold, partner.Gold);

        // ��������� �������� � ���������� � ����� �� �������
        foreach (var e in session.InitiatorItemIds)
        {
            var proto = initiator.Inventory.FirstOrDefault(i => i.Id == e.ItemId);
            if (proto == null) continue;
            var copy = MakeCopy(proto, e.Quantity);
            InventoryHelper.RemoveFromRecord(initiator, e.ItemId, e.Quantity);
            InventoryHelper.AddItem(partner, copy);
        }

        // ��������� �������� � ������� � ����� �� ����������
        foreach (var e in session.PartnerItemIds)
        {
            var proto = partner.Inventory.FirstOrDefault(i => i.Id == e.ItemId);
            if (proto == null) continue;
            var copy = MakeCopy(proto, e.Quantity);
            InventoryHelper.RemoveFromRecord(partner, e.ItemId, e.Quantity);
            InventoryHelper.AddItem(initiator, copy);
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
            Data = new { Success = true, Message = "����� ������� ��������!" }
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

        Svc.Trade.RemoveSession(session);

        int iniTotal = session.InitiatorItemIds.Sum(e => e.Quantity);
        int parTotal = session.PartnerItemIds.Sum(e => e.Quantity);
        Log.Info($"����� ��������: {initiator.Name} - {partner.Name} | " +
                 $"{initiator.Name} ����� {iniTotal} ��������� + {initiatorGold} ������; " +
                 $"{partner.Name} ����� {parTotal} ��������� + {partnerGold} ������");
    }

    private static Item MakeCopy(Item proto, int qty)
    {
        var copy = proto.Clone();
        copy.Id = Guid.NewGuid().ToString();
        copy.Quantity = qty;
        return copy;
    }

    private static bool ValidateFinalOffer(Player player, List<TradeOfferEntry> entries, int gold)
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

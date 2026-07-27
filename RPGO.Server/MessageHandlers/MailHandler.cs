using RPGGame.Server.Repositories;
using RPGGame.Server.Services;
using RPGGame.Server.Network;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;
using System.Text.Json;

namespace RPGGame.Server.MessageHandlers;

public class MailHandler : BaseHandler
{
    public MailHandler(GameWorld world, INetworkHub hub) : base(world, hub) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;
        string action = "";
        if (message.Data is JsonElement el && el.TryGetProperty("Action", out var actEl))
            action = actEl.GetString() ?? "";

        switch (action.ToLowerInvariant())
        {
            case "inbox": await HandleInbox(connection, player); break;
            case "outbox": await HandleOutbox(connection, player); break;
            case "send": await HandleSend(connection, player, message); break;
            case "read": await HandleRead(connection, player, message); break;
            case "take": await HandleTake(connection, player, message); break;
            case "delete": await HandleDelete(connection, player, message); break;
        }
    }

    private async Task HandleInbox(ClientConnection connection, Player player)
    {
        var mails = MailRepository.GetInbox(player.Name);
        await SendToClient(connection, new GameMessage
        {
            Type = "mail_list",
            Data = new { Folder = "inbox", Mails = mails.Select(m => MapMail(m)).ToList() }
        });
        int unread = MailRepository.CountUnread(player.Name);
        await SendToClient(connection, new GameMessage { Type = "mail_unread", Data = new { Count = unread } });
    }

    private async Task HandleOutbox(ClientConnection connection, Player player)
    {
        var mails = MailRepository.GetOutbox(player.Name);
        await SendToClient(connection, new GameMessage
        {
            Type = "mail_list",
            Data = new { Folder = "outbox", Mails = mails.Select(m => MapMail(m)).ToList() }
        });
    }

    private async Task HandleSend(ClientConnection connection, Player player, GameMessage message)
    {
        if (message.Data is not JsonElement el) return;

        string recipient = el.TryGetProperty("RecipientName", out var rn) ? (rn.GetString() ?? "") : "";
        string subject = el.TryGetProperty("Subject", out var sj) ? (sj.GetString() ?? "") : "";
        string body = el.TryGetProperty("Body", out var bd) ? (bd.GetString() ?? "") : "";
        int gold = el.TryGetProperty("GoldAmount", out var ga) ? ga.GetInt32() : 0;

        Item? attachment = null;
        string attachItemId = el.TryGetProperty("ItemId", out var ii) ? (ii.GetString() ?? "") : "";
        int attachQty = el.TryGetProperty("ItemQuantity", out var iq) ? iq.GetInt32() : 0;

        if (!string.IsNullOrEmpty(attachItemId) && attachQty > 0)
        {
            var invItem = player.Inventory.FirstOrDefault(i => i.TemplateId == attachItemId);
            if (invItem == null)
            {
                await SendError(connection, "mail_error", "Предмет не найден в инвентаре.");
                return;
            }
            if (invItem.Quantity < attachQty)
            {
                await SendError(connection, "mail_error", "Недостаточно предметов.");
                return;
            }
            attachment = new Item
            {
                Id = invItem.Id,
                TemplateId = invItem.TemplateId,
                Name = invItem.Name,
                Type = invItem.Type,
                Quantity = attachQty,
                Value = invItem.Value,
            };
        }

        var (id, error) = MailManager.SendMail(player, recipient, subject, body, gold, attachment);
        if (!string.IsNullOrEmpty(error))
        {
            await SendError(connection, "mail_error", error);
            return;
        }

        await SendToClient(connection, new GameMessage
        {
            Type = "mail_result",
            Data = new { Success = true, Message = "Письмо отправлено.", MailId = id }
        });

        await SendInventoryAndStatus(connection, player);

        var recipientPlayer = World.TryGetPlayerByName(recipient, out var rp) ? rp : null;
        if (recipientPlayer != null)
        {
            var recipientConn = World.FindClientByPlayer(recipientPlayer);
            if (recipientConn != null)
            {
                int unread = MailRepository.CountUnread(recipient);
                await SendToClient(recipientConn, new GameMessage
                {
                    Type = "mail_unread",
                    Data = new { Count = unread }
                });
                await SendChatToAsync(recipientConn, Shared.Network.ChatChannel.System, "Почта",
                    $"Новое письмо от {player.Name}!");
            }
        }
    }

    private async Task HandleRead(ClientConnection connection, Player player, GameMessage message)
    {
        if (message.Data is not JsonElement el) return;
        int mailId = el.TryGetProperty("MailId", out var mid) ? mid.GetInt32() : 0;
        if (mailId <= 0) return;

        var mail = MailRepository.GetById(mailId);
        if (mail == null) return;
        if (mail.SenderName != player.Name && mail.RecipientName != player.Name) return;

        MailRepository.MarkRead(mailId);
        mail.ReadAt = DateTime.UtcNow.ToString("o");

        await SendToClient(connection, new GameMessage
        {
            Type = "mail_detail",
            Data = MapMail(mail)
        });

        int unread = MailRepository.CountUnread(player.Name);
        await SendToClient(connection, new GameMessage { Type = "mail_unread", Data = new { Count = unread } });
    }

    private async Task HandleTake(ClientConnection connection, Player player, GameMessage message)
    {
        if (message.Data is not JsonElement el) return;
        int mailId = el.TryGetProperty("MailId", out var mid) ? mid.GetInt32() : 0;
        if (mailId <= 0) return;

        var mail = MailRepository.GetById(mailId);
        if (mail == null || mail.RecipientName != player.Name)
        {
            await SendError(connection, "mail_error", "Письмо не найдено.");
            return;
        }

        if (mail.TakenAt != "")
        {
            await SendError(connection, "mail_error", "Вложение уже получено.");
            return;
        }

        bool ok = MailManager.TakeAttachment(player, mail);
        if (!ok)
        {
            await SendError(connection, "mail_error", "Не удалось забрать вложение.");
            return;
        }

        await SendToClient(connection, new GameMessage
        {
            Type = "mail_result",
            Data = new { Success = true, Message = "Вложение получено." }
        });

        await SendToClient(connection, new GameMessage
        {
            Type = "mail_detail",
            Data = MapMail(mail)
        });

        await SendInventoryAndStatus(connection, player);
    }

    private async Task HandleDelete(ClientConnection connection, Player player, GameMessage message)
    {
        if (message.Data is not JsonElement el) return;
        int mailId = el.TryGetProperty("MailId", out var mid) ? mid.GetInt32() : 0;
        if (mailId <= 0) return;

        var mail = MailRepository.GetById(mailId);
        if (mail == null) return;
        if (mail.SenderName != player.Name && mail.RecipientName != player.Name) return;

        MailRepository.DeleteMail(mailId, player.Name);

        await SendToClient(connection, new GameMessage
        {
            Type = "mail_result",
            Data = new { Success = true, Message = "Письмо удалено." }
        });

        int unread = MailRepository.CountUnread(player.Name);
        await SendToClient(connection, new GameMessage { Type = "mail_unread", Data = new { Count = unread } });
    }

    private static object MapMail(MailData m) => new
    {
        m.Id, m.SenderName, m.RecipientName, m.Subject, m.Body,
        m.GoldAmount, m.ItemId, m.ItemName, m.ItemType, m.ItemQuantity,
        m.SentAt, m.ReadAt, m.TakenAt
    };
}

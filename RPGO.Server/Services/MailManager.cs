using RPGGame.Server.Repositories;
using RPGGame.Shared.Models;

namespace RPGGame.Server.Services;

public static class MailManager
{
    private const int MaxSubjectLength = 50;
    private const int MaxBodyLength = 200;
    private const int MaxGoldPerMail = 100000;

    public static (int Id, string Error) SendMail(Player sender, string recipient, string subject, string body,
        int goldAmount, Item? attachment)
    {
        if (string.IsNullOrWhiteSpace(recipient))
            return (0, "Укажите получателя.");
        if (recipient == sender.Name)
            return (0, "Нельзя писать самому себе.");
        if (!MailRepository.PlayerExists(recipient))
            return (0, $"Игрок «{recipient}» не найден.");
        if (goldAmount < 0)
            return (0, "Золото не может быть отрицательным.");
        if (goldAmount > MaxGoldPerMail)
            return (0, $"Максимум {MaxGoldPerMail} золота за письмо.");

        subject = (subject ?? "").Trim().Substring(0, Math.Min(subject.Length, MaxSubjectLength));
        body = (body ?? "").Trim().Substring(0, Math.Min(body.Length, MaxBodyLength));

        if (goldAmount > 0)
        {
            if (sender.Gold < goldAmount)
                return (0, "Недостаточно золота.");
            sender.Gold -= goldAmount;
        }

        string itemId = "";
        string itemName = "";
        string itemType = "";
        int itemQty = 0;

        if (attachment != null)
        {
            itemId = attachment.TemplateId;
            itemName = attachment.Name;
            itemType = attachment.Type;
            itemQty = attachment.Quantity;

            InventoryHelper.RemoveFromRecord(sender, attachment.Id, itemQty);
        }

        int id = MailRepository.Send(sender.Name, recipient, subject, body,
            goldAmount, itemId, itemName, itemType, itemQty);

        if (id < 0)
        {
            if (goldAmount > 0) sender.Gold += goldAmount;
            if (attachment != null)
                InventoryHelper.AddItem(sender, attachment);
            return id == -1 ? (0, "Почта получателя заполнена.") : (0, "Ваша почта заполнена.");
        }

        return (id, "");
    }

    public static bool TakeAttachment(Player recipient, MailData mail)
    {
        if (mail.TakenAt != "") return false;
        if (mail.RecipientName != recipient.Name) return false;

        if (mail.GoldAmount > 0)
            recipient.Gold += mail.GoldAmount;

        if (!string.IsNullOrEmpty(mail.ItemId) && mail.ItemQuantity > 0)
        {
            var template = DatabaseManager.GetItemTemplate(mail.ItemId);
            if (template != null)
            {
                var item = new Item
                {
                    TemplateId = template.Id,
                    Name = template.Name,
                    Type = template.Type,
                    Value = template.Value,
                    BonusDefense = template.BonusDefense,
                    MaxHealthBonus = template.MaxHealthBonus,
                    HealAmount = template.HealAmount,
                    RestoreMana = template.RestoreMana,
                    Description = template.Description,
                    Quantity = mail.ItemQuantity
                };
                InventoryHelper.AddItem(recipient, item);
            }
        }

        MailRepository.TakeAttachment(mail.Id);
        mail.TakenAt = DateTime.UtcNow.ToString("o");
        return true;
    }
}

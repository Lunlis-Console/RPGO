using LostAndDivine.Server.Repositories;
using LostAndDivine.Shared.Models;

namespace LostAndDivine.Server.Services;

public static class MailManager
{
    private const int MaxSubjectLength = 48;
    private const int MaxBodyLength = 200;
    private const int MaxGoldPerMail = 100000;

    public static (int Id, string Error) SendMail(Player sender, string recipient, string subject, string body,
        int goldAmount, List<Item>? attachments)
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

        string trimmedSubject = (subject ?? "").Trim();
        subject = trimmedSubject.Substring(0, Math.Min(trimmedSubject.Length, MaxSubjectLength));
        string trimmedBody = (body ?? "").Trim();
        body = trimmedBody.Substring(0, Math.Min(trimmedBody.Length, MaxBodyLength));

        if (goldAmount > 0)
        {
            if (sender.Gold < goldAmount)
                return (0, "Недостаточно золота.");
            sender.Gold -= goldAmount;
        }

        var removed = new List<Item>();
        var attachmentRecords = new List<MailAttachment>();

        if (attachments != null)
        {
            foreach (var att in attachments)
            {
                if (att == null || att.Quantity <= 0) continue;
                if (string.IsNullOrEmpty(att.TemplateId))
                    continue;

                int available = InventoryHelper.CountByItem(sender, att);
                if (available < att.Quantity)
                {
                    // Откат: вернуть золото и уже снятые предметы
                    if (goldAmount > 0) sender.Gold += goldAmount;
                    foreach (var r in removed) InventoryHelper.AddItem(sender, r);
                    return (0, $"Недостаточно предметов «{att.Name}».");
                }

                InventoryHelper.RemoveQuantity(sender, att, att.Quantity);
                removed.Add(att.Clone());
                attachmentRecords.Add(new MailAttachment
                {
                    TemplateId = att.TemplateId,
                    Name = att.Name,
                    Type = att.Type,
                    Quantity = att.Quantity,
                    WeaponSubtype = att.WeaponSubtype ?? "",
                    HealAmount = att.HealAmount,
                    RestoreMana = att.RestoreMana
                });
            }
        }

        int id = MailRepository.Send(sender.Name, recipient, subject, body,
            goldAmount, attachmentRecords);

        if (id < 0)
        {
            if (goldAmount > 0) sender.Gold += goldAmount;
            foreach (var r in removed) InventoryHelper.AddItem(sender, r);
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

        foreach (var att in mail.Attachments)
        {
            if (string.IsNullOrEmpty(att.TemplateId) || att.Quantity <= 0) continue;
            var item = DatabaseManager.GetItemTemplate(att.TemplateId);
            if (item != null)
            {
                item.Id = Guid.NewGuid().ToString();
                item.Quantity = att.Quantity;
                InventoryHelper.AddItem(recipient, item);
            }
        }

        MailRepository.TakeAttachment(mail.Id);
        mail.TakenAt = DateTime.UtcNow.ToString("o");
        return true;
    }
}

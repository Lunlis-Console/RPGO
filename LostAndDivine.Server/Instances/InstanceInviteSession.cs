using LostAndDivine.Shared.Models;

namespace LostAndDivine.Server.Instances;

public enum InstanceInviteStatus
{
    Waiting,
    Ready,
    Declined
}

/// <summary>Сессия приглашения в групповой инстанс: лидер зовёт членов группы,
/// каждый отвечает Готов/Отмена; когда все неотказавшиеся готовы — автостарт,
/// либо лидер запускает вручную кнопкой «Начать».</summary>
public class InstanceInviteSession
{
    public Guid Id { get; } = Guid.NewGuid();
    public string TemplateId { get; }
    public string TemplateName { get; }
    public Player Leader { get; }
    public Dictionary<Guid, InstanceInviteStatus> Statuses { get; } = new();
    public bool Started { get; set; }
    public DateTime ExpiresAt { get; } = DateTime.UtcNow.AddMinutes(2);

    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;

    public InstanceInviteSession(Player leader, string templateId, string templateName)
    {
        Leader = leader;
        TemplateId = templateId;
        TemplateName = templateName;
    }
}
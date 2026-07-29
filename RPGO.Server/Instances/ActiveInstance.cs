using RPGGame.Shared.Models;

namespace RPGGame.Server.Instances;

public class ActiveInstance
{
    public Guid Id { get; } = Guid.NewGuid();
    public string InstanceZoneId => $"instance:{Id:N}";
    public InstanceTemplate Template { get; }
    public GameMap Map { get; }
    public List<Monster> Monsters { get; } = new();
    public List<Player> Players { get; } = new();
    public bool ChestLocked { get; set; } = true;
    public DateTime CreatedAt { get; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; }

    public int OffsetX { get; }
    public int OffsetY { get; }

    public ActiveInstance(InstanceTemplate template, GameMap map)
    {
        Template = template;
        Map = map;
        ExpiresAt = CreatedAt.AddSeconds(template.TimeLimitSeconds);
        var (ox, oy) = InstanceManager.GetCorridorOffset(template);
        OffsetX = ox;
        OffsetY = oy;
    }

    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
    public bool IsBossAlive => Monsters.Any(m => m.TemplateId == Template.BossMonsterId && m.Health > 0);
}

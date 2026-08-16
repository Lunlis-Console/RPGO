using LostAndDivine.Shared.Models;

namespace LostAndDivine.Server.Instances;

public class ActiveInstance
{
    public Guid Id { get; } = Guid.NewGuid();
    public string InstanceZoneId => $"instance:{Id:N}";
    public InstanceTemplate Template { get; }
    public GameMap Map { get; }
    public InstanceMode Mode { get; }
    public List<Monster> Monsters { get; } = new();
    public List<Player> Players { get; } = new();
    public bool ChestLocked { get; set; } = true;

    /// <summary>Индивидуальная награда сундука для каждого игрока (не общий пул).</summary>
    public Dictionary<Guid, InstanceChestReward> ChestRewards { get; } = new();
    public DateTime CreatedAt { get; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; }

    public int OffsetX { get; }
    public int OffsetY { get; }

    /// <summary>Кастомная точка спавна (для Tiled-карт). 0 — использовать Offset.</summary>
    internal int _spawnX;
    internal int _spawnY;
    internal int _chestX;
    internal int _chestY;
    internal int _exitX;
    internal int _exitY;

    public int EffectiveChestX => _chestX > 0 ? _chestX : Template.ChestX + OffsetX;
    public int EffectiveChestY => _chestY > 0 ? _chestY : Template.ChestY + OffsetY;
    public int EffectiveExitX => _exitX > 0 ? _exitX : Template.ExitX + OffsetX;
    public int EffectiveExitY => _exitY > 0 ? _exitY : Template.ExitY + OffsetY;

    public ActiveInstance(InstanceTemplate template, GameMap map, InstanceMode mode = InstanceMode.Solo)
    {
        Template = template;
        Map = map;
        Mode = mode;
        ExpiresAt = CreatedAt.AddSeconds(template.TimeLimitSeconds);
        var (ox, oy) = InstanceManager.GetCorridorOffset(template);
        OffsetX = ox;
        OffsetY = oy;
    }

    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
    public bool IsBossAlive => Monsters.Any(m => m.TemplateId == Template.BossMonsterId && m.Health > 0);
}

/// <summary>Индивидуальный дроп сундука: каждый игрок инстанса получает свой ролл.</summary>
public class InstanceChestReward
{
    public int Gold { get; set; }
    public List<Item> Items { get; } = new();
}

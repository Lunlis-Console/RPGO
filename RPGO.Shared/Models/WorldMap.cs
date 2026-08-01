namespace RPGGame.Shared.Models;

/// <summary>
/// DTO, отправляемое клиенту (позиции сущностей + размеры мира). Не является игровым
/// состоянием — размеры заполняются сервером из GameMap при отправке.
/// </summary>
public class WorldMap
{
    public int Width { get; set; } = 100;
    public int Height { get; set; } = 100;

    public List<PlayerPosition> Players { get; set; } = new();
    public MerchantPosition? Merchant { get; set; }
    public QuestBoardPosition? Board { get; set; }
    public List<MonsterPosition> Monsters { get; set; } = new();
    public List<CollectiblePosition> Collectibles { get; set; } = new();
    public List<CorpsePosition> Corpses { get; set; } = new();
    public List<NpcPosition> Npcs { get; set; } = new();

    // Зона
    public string ZoneId { get; set; } = "main";
    public string ZoneName { get; set; } = "";
    public bool PvPEnabled { get; set; }
    public List<PortalPosition> Portals { get; set; } = new();
    public PortalPosition? InstanceExitPortal { get; set; }
    public ChestPosition? InstanceChest { get; set; }
    public double? InstanceExpiresAtUtcMs { get; set; }

    public List<HazardPosition> Hazards { get; set; } = new();

    // Тайл-карта
    public string? TileMapId { get; set; }
    public byte[]? TileData { get; set; }
    public byte[]? ObstacleData { get; set; }
    public int TileWidth { get; set; } = 32;
    public int TileHeight { get; set; } = 32;
    public string? TilesetId { get; set; }
}

public class PlayerPosition
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int Level { get; set; }
    public int Health { get; set; }
    public int MaxHealth { get; set; }
    public string Facing { get; set; } = "down";
    public string WeaponSubtype { get; set; } = "";
    public string OffWeaponSubtype { get; set; } = "";
    public string ShieldSubtype { get; set; } = "";
    public bool IsTwoHanded { get; set; }
    public bool IsDead { get; set; }
}

public class MerchantPosition
{
    public int X { get; set; }
    public int Y { get; set; }
    public string Name { get; set; } = "Торговец";
}

public class CollectiblePosition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string ItemName { get; set; } = "";
    public char Symbol { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public string ZoneId { get; set; } = "main";
}

public class CorpsePosition
{
    public Guid Id { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public string MonsterName { get; set; } = "";
    public char Symbol { get; set; }
    public int Level { get; set; }
    public int ItemCount { get; set; }
    public string ZoneId { get; set; } = "main";
}

public class NpcPosition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public string ZoneId { get; set; } = "main";
    public bool HasDialogue { get; set; }
    public string? QuestIndicator { get; set; }
}

public class PortalPosition
{
    public int X { get; set; }
    public int Y { get; set; }
    public string TargetZone { get; set; } = "";
    public string TargetZoneName { get; set; } = "";
}

public class ChestPosition
{
    public int X { get; set; }
    public int Y { get; set; }
    public bool IsLocked { get; set; }
}
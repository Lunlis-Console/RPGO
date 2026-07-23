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
    public int X { get; set; }
    public int Y { get; set; }
    public string Name { get; set; } = "";
    public string ItemName { get; set; } = "";
    public char Symbol { get; set; }
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
}

public class NpcPosition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public bool HasDialogue { get; set; }
    public string? QuestIndicator { get; set; }
}
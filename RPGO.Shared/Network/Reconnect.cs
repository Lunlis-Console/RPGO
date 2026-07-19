namespace RPGGame.Shared.Network;

public record ReconnectRequest(Guid PlayerId, string Token, long LastSeq);

public record ReconnectResponse(bool Success, string? Reason, PlayerState? Player);

public record PlayerState(
    string Name,
    int Level,
    int X,
    int Y,
    int Health,
    int MaxHealth,
    int Mana,
    int MaxMana,
    long Experience,
    int AttributePoints,
    int Strength,
    int Stamina,
    int Agility,
    int Cunning,
    int Wisdom,
    int Will,
    int Attack,
    int Defense,
    int Gold,
    List<ItemState> Inventory,
    List<HotbarSlotState> Hotbar,
    List<ActiveQuestState> ActiveQuests
);

public record ItemState(string Id, int Count, int? Slot);

public record HotbarSlotState(string Type, string Data);

public record ActiveQuestState(string QuestId, int Current, bool Completed);
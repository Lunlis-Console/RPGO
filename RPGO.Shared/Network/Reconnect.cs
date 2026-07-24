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
    int Endurance,
    int Agility,
    int Cunning,
    int Intellect,
    int Wisdom,
    int Attack,
    int Defense,
    int Gold,
    List<ItemState> Inventory,
    List<HotbarSlotState> Hotbar,
    List<ActiveQuestState> ActiveQuests,
    List<DebuffState> ActiveDebuffs
);

public record DebuffState(string Type, string DisplayName, string Description, double Value, int RemainingMs, int DurationMs);

public record ItemState(string Id, int Count, int? Slot);

public record HotbarSlotState(string Type, string Data);

public record ActiveQuestState(string QuestId, int Current, bool Completed);
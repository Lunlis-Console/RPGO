namespace RPGGame.Shared.Models;

/// <summary>
/// Описание одного слота экипировки. Единый источник истины для сервера,
/// клиента и редактора. Видимые слои (IsPaperDollLayer) рисуются на аватаре
/// в порядке ZOrder (от дальнего к ближнему).
/// </summary>
public sealed record EquipmentSlotDef(
    string Id,
    string NameRu,
    bool IsPaperDollLayer,
    int ZOrder,              // для видимых слоёв: больше = ближе к камере (рисуется поверх)
    bool AcceptsTwoHanded,  // слот может держать двуручное оружие (только правая рука)
    bool BlockedByTwoHanded // слот блокируется, когда двуручное оружие надето в основную руку
);

/// <summary>
/// Авторитетный список слотов экипировки персонажа.
/// Согласован с миграцией 009 (таблица equipment_slots).
/// </summary>
public static class EquipmentSlots
{
    // Игровые слоты (12)
    public const string Cloak = "cloak";
    public const string Head = "head";
    public const string Torso = "torso";
    public const string Legs = "legs";
    public const string Feet = "feet";
    public const string RightHand = "rhand";
    public const string LeftHand = "lhand";
    public const string RightGlove = "glove_r";
    public const string LeftGlove = "glove_l";
    public const string Neck = "neck";
    public const string RingRight = "ring_r";
    public const string RingLeft = "ring_l";

    public static IReadOnlyList<EquipmentSlotDef> All { get; } = new List<EquipmentSlotDef>
    {
        // Порядок соответствует сетке снаряжения (3 столбца):
        //  Плащ · Шлем · Ожерелье
        //  Правая рука · Торс · Левая рука
        //  Правая перчатка · Ноги · Левая перчатка
        //  Кольцо(п.р.) · Обувь · Кольцо(л.р.)
        new(Cloak,     "Плащ",            false, 0, false, false),
        new(Head,      "Шлем",            true,  4, false, false),
        new(Neck,      "Ожерелье",        false, 0, false, false),
        new(RightHand, "Правая рука",     true,  8, true,  false),
        new(Torso,     "Торс",            true,  3, false, false),
        new(LeftHand,  "Левая рука",      true,  6, false, true),
        new(RightGlove,"Правая перчатка", true,  7, false, false),
        new(Legs,      "Ноги",            true,  1, false, false),
        new(LeftGlove, "Левая перчатка",  true,  5, false, false),
        new(RingRight, "Кольцо (правая рука)", false, 0, false, false),
        new(Feet,      "Обувь",           true,  2, false, false),
        new(RingLeft,  "Кольцо (левая рука)",  false, 0, false, false),
    };

    /// <summary>Видимые слои бумажной куклы, от дальнего к ближнему (по ZOrder).</summary>
    public static IReadOnlyList<EquipmentSlotDef> PaperDollLayers { get; } =
        All.Where(s => s.IsPaperDollLayer).OrderBy(s => s.ZOrder).ToList();

    private static readonly Dictionary<string, EquipmentSlotDef> _byId = All.ToDictionary(s => s.Id, s => s);

    public static EquipmentSlotDef? Get(string id) => _byId.TryGetValue(id, out var d) ? d : null;

    public static bool Exists(string id) => _byId.ContainsKey(id);

    /// <summary>Можно ли надеть двуручное оружие в этот слот (только правая рука).</summary>
    public static bool CanHoldTwoHanded(string id) => Get(id)?.AcceptsTwoHanded ?? false;

    /// <summary>Блокируется ли слот, когда двуручное оружие надето в основную руку (левая рука).</summary>
    public static bool IsBlockedByTwoHanded(string id) => Get(id)?.BlockedByTwoHanded ?? false;

    /// <summary>Заблокирован ли слот прямо сейчас (с учётом надетого в основную руку оружия).</summary>
    public static bool IsBlockedByTwoHanded(string slotId, Equipment equipment)
    {
        if (!IsBlockedByTwoHanded(slotId)) return false;
        var rh = equipment[RightHand];
        return rh != null && IsTwoHanded(rh.Type, rh.TwoHanded);
    }

    /// <summary>В какие слоты можно надеть предмет данного типа (в порядке приоритета).</summary>
    public static IReadOnlyList<string> SlotsForItemType(string? type)
    {
        var t = (type ?? "").ToLowerInvariant();
        return _typeToSlots.TryGetValue(t, out var list) ? list : Array.Empty<string>();
    }

    /// <summary>Можно ли вообще надеть предмет этого типа.</summary>
    public static bool IsEquippableType(string? type) =>
        !string.IsNullOrEmpty(type) && _typeToSlots.ContainsKey((type!).ToLowerInvariant());

    /// <summary>Двуручное ли это оружие (по типу или флагу предмета).</summary>
    public static bool IsTwoHanded(string? type, bool itemTwoHanded) =>
        itemTwoHanded || (type ?? "").ToLowerInvariant() == "twohand";

    private static readonly Dictionary<string, List<string>> _typeToSlots = new()
    {
        ["weapon"]   = new() { RightHand },
        ["twohand"]  = new() { RightHand },          // двуручное: только основная рука, блокирует левую
        ["shield"]   = new() { LeftHand },           // щит/оффхенд
        ["helmet"]   = new() { Head },
        ["cloak"]    = new() { Cloak },
        ["chest"]    = new() { Torso },
        ["legs"]     = new() { Legs },
        ["boots"]    = new() { Feet },
        ["glove_r"]  = new() { RightGlove }, // отдельно правая перчатка
        ["glove_l"]  = new() { LeftGlove },  // отдельно левая перчатка
        ["necklace"] = new() { Neck },
        ["ring"]     = new() { RingRight, RingLeft },   // кольцо — в любую свободную руку
        // Обратная совместимость со старыми типами
        ["armor"]    = new() { Torso },
        ["accessory"]= new() { Neck },
    };
}

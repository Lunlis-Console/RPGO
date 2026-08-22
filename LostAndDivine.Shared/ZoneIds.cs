namespace LostAndDivine.Shared;

/// <summary>
/// Канонические идентификаторы зон. Единый источник истины для строковых ID зон,
/// чтобы в коде не размножались магические строки ("main", "airship_basement").
/// Динамические зоны инстансов генерируются отдельно и здесь не перечисляются.
/// </summary>
public static class ZoneIds
{
    public const string Main = "main";
    public const string Start = "airship_basement";
}

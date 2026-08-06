namespace LostAndDivine.Shared.Models;

public enum ItemQuality
{
    Common = 0,
    Uncommon = 1,
    Rare = 2,
    Epic = 3
}

public static class ItemQualityExtensions
{
    public static ItemQuality ParseFromDescription(string? description)
    {
        if (string.IsNullOrEmpty(description)) return ItemQuality.Common;
        if (description.Contains("Необычный")) return ItemQuality.Uncommon;
        if (description.Contains("Редкий")) return ItemQuality.Rare;
        if (description.Contains("Эпический")) return ItemQuality.Epic;
        return ItemQuality.Common;
    }

    public static string Label(ItemQuality quality) => quality switch
    {
        ItemQuality.Uncommon => "Необычный",
        ItemQuality.Rare => "Редкий",
        ItemQuality.Epic => "Эпический",
        _ => "Обычный"
    };
}

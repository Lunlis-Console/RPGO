namespace LostAndDivine.Shared.Models;

/// <summary>
/// Инвентарная часть игрока: вещи, экипировка, хотбар, выкуп.
/// Вынесено из God-Class Player.cs для SRP.
/// </summary>
public sealed class PlayerInventory
{
    public List<Item> Inventory { get; set; } = new();
    public Equipment Equipment { get; set; } = new();
    public List<string?> HotbarSlots { get; set; } = new(10) { null, null, null, null, null, null, null, null, null, null };
    public List<Item> BuybackItems { get; set; } = new();
}

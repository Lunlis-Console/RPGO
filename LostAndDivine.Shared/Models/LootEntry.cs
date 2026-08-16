namespace LostAndDivine.Shared.Models;

/// <summary>Строка дропа монстра: предмет-шаблон и шанс выпадения (0-100).</summary>
public class MonsterDrop
{
    public string MonsterId = "";
    public string ItemId = "";
    public int DropChance;
}
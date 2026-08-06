namespace LostAndDivine.Shared.Models;

public class InstanceSpawn
{
    public string Id { get; set; } = "";
    public string InstanceTemplateId { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public string MonsterTemplateId { get; set; } = "";
    public bool IsBoss { get; set; }
}

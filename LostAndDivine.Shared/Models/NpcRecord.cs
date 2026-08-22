namespace LostAndDivine.Shared.Models;

public class NpcRecord
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    [System.Text.Json.Serialization.JsonIgnore]
    public NpcType TypeEnum { get => NpcTypeExtensions.Parse(Type); set => Type = value.ToDisplayString(); }
    public int X { get; set; }
    public int Y { get; set; }
    public int WanderRadius { get; set; }
    public string? Data { get; set; }
}

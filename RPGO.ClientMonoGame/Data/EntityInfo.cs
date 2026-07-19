namespace RPGGame.ClientMonoGame.Data;

public sealed class EntityInfo
{
    public string Type { get; set; } = "";
    public string Name { get; set; } = "";
    public int Level { get; set; }
    public int Hp { get; set; }
    public int MaxHp { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public string? Id { get; set; }
}

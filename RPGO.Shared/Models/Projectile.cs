namespace RPGGame.Shared.Models;

public class Projectile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public double StartX { get; set; }
    public double StartY { get; set; }
    public double CurrentX { get; set; }
    public double CurrentY { get; set; }
    public double TargetX { get; set; }
    public double TargetY { get; set; }
    public string VisualType { get; set; } = "arrow";
    public int Damage { get; set; }
    public bool IsCrit { get; set; }
    public Guid OwnerId { get; set; }
    public string OwnerName { get; set; } = "";
    public Guid TargetMonsterId { get; set; }
    public DateTime SpawnTime { get; set; } = DateTime.UtcNow;
}

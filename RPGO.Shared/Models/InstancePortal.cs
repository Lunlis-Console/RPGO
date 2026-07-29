namespace RPGGame.Shared.Models;

public class InstancePortal
{
    public string Id { get; set; } = "";
    public string FromZone { get; set; } = "";
    public int FromX { get; set; }
    public int FromY { get; set; }
    public string InstanceTemplateId { get; set; } = "";
}

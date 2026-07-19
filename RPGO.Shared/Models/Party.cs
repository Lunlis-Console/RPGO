namespace RPGGame.Shared.Models;

public class PartyInfo
{
    public Guid PartyId { get; set; }
    public Guid LeaderId { get; set; }
    public string LeaderName { get; set; } = "";
    public List<PartyMemberInfo> Members { get; set; } = new();
}

public class PartyMemberInfo
{
    public Guid PlayerId { get; set; }
    public string Name { get; set; } = "";
    public int Health { get; set; }
    public int MaxHealth { get; set; }
    public int Level { get; set; }
}

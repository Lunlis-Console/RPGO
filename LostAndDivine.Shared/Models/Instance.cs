namespace LostAndDivine.Shared.Models;

public class InstanceInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int MinLevel { get; set; }
    public int MaxLevel { get; set; }
}

public class InstanceMemberInfo
{
    public string Name { get; set; } = "";
    public string Status { get; set; } = ""; // ready | waiting | declined
}
namespace RPGGame.Shared.Commands;

public class MoveDirectionCommand
{
    public string Direction { get; set; } = ""; // "up", "down", "left", "right"
}

public class MoveToCommand
{
    public int X { get; set; }
    public int Y { get; set; }
}
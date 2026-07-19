namespace RPGGame.Shared.Commands;

public class RegisterCommand
{
    public string Login { get; set; } = "";
    public string Password { get; set; } = "";
    public string PlayerName { get; set; } = "";
}

public class LoginAuthCommand
{
    public string Login { get; set; } = "";
    public string Password { get; set; } = "";
}
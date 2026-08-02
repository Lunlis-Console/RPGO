using RPGGame.Server.Network;
using RPGGame.Server.Repositories;
using RPGGame.Server.Services;
using RPGGame.Shared.Commands;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;
using System.Text.Json;

namespace RPGGame.Server;

/// <summary>
/// Обработка регистрации и входа игроков.
/// Вынесена из Program.Auth.cs.
/// </summary>
public class AuthService
{
    private readonly Lazy<GameServices> _svcLazy;
    private GameServices _svc => _svcLazy.Value;

    public AuthService(Lazy<GameServices> svc)
    {
        _svcLazy = svc;
    }

    public async Task<bool> HandleAuthMessage(ClientConnection connection, GameMessage message, INetworkHub hub)
    {
        switch (message.Type)
        {
            case "register":
                string registerJson = JsonSerializer.Serialize(message.Data);
                var registerData = JsonSerializer.Deserialize<RegisterCommand>(registerJson);

                if (registerData != null)
                {
                    var (success, account) = DatabaseManager.Register(
                        registerData.Login,
                        registerData.Password,
                        registerData.PlayerName
                    );

                    if (success && account != null)
                    {
                        await hub.SendToClient(connection, new GameMessage
                        {
                            Type = "auth_response",
                            Data = new { Success = true, Message = $"Регистрация успешна! Добро пожаловать, {account.PlayerName}!" }
                        });
                        Log.Info($"Зарегистрирован новый игрок: {account.Login} ({account.PlayerName})");
                        return false;
                    }
                    else
                    {
                        await hub.SendToClient(connection, new GameMessage
                        {
                            Type = "auth_response",
                            Data = new { Success = false, Message = "Ошибка регистрации. Логин или имя уже заняты." }
                        });
                    }
                }
                break;

            case "login_auth":
                string loginJson = JsonSerializer.Serialize(message.Data);
                var loginData = JsonSerializer.Deserialize<LoginAuthCommand>(loginJson);

                if (loginData != null)
                {
                    var (success, account) = DatabaseManager.Login(loginData.Login, loginData.Password);

                    if (success && account != null)
                    {
                        if (account.IsBanned)
                        {
                            await hub.SendToClient(connection, new GameMessage
                            {
                                Type = "auth_response",
                                Data = new { Success = false, Message = $"Вы заблокированы. Причина: {account.BanReason}" }
                            });
                            Log.Info($"Заблокированный игрок пытался войти: {account.Login}");
                            return false;
                        }

                        var existingSession = _svc.World.GetConnectionByPlayerName(account.PlayerName);
                        if (existingSession != null)
                        {
                            await hub.SendToClient(connection, new GameMessage
                            {
                                Type = "auth_response",
                                Data = new { Success = false, Message = "Этот аккаунт уже в игре. Выйдите из другого клиента и повторите вход." }
                            });
                            Log.Info($"Попытка повторного входа: {account.Login} ({account.PlayerName}) уже в игре ({existingSession.Endpoint})");
                            return false;
                        }

                        var player = PlayerFactory.FromAccount(account, _svc);

                        _svc.World.AddPlayer(player);
                        connection.Player = player;

                        var sessionToken = SessionManager.CreateToken(player.Name);

                        await hub.SendToClient(connection, new GameMessage
                        {
                            Type = "auth_response",
                            Data = new { Success = true, Message = $"Добро пожаловать, {player.Name}!", session_token = sessionToken, player_id = player.Id }
                        });

                        await hub.SendToClient(connection, new GameMessage
                        {
                            Type = "welcome",
                            Data = new { Message = $"Добро пожаловать, {player.Name}!", PlayerName = player.Name }
                        });

                        Log.Info($"Игрок {player.Name} вошел в мир на позиции ({player.X}, {player.Y})");
                        await hub.BroadcastMapAsync();
                        await hub.SendQuestLog(connection, player);
                        await hub.SendHotbar(connection, player);
                        await hub.SendInventoryAndStatus(connection, player);

                        int unreadCount = MailRepository.CountUnread(player.Name);
                        await hub.SendToClient(connection, new GameMessage
                        {
                            Type = "mail_unread",
                            Data = new { Count = unreadCount }
                        });

                        return true;
                    }
                    else
                    {
                        await hub.SendToClient(connection, new GameMessage
                        {
                            Type = "auth_response",
                            Data = new { Success = false, Message = "Неверный логин или пароль!" }
                        });
                    }
                }
                break;
        }

        return false;
    }
}

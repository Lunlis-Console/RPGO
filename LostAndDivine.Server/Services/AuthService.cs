using LostAndDivine.Server.Network;
using LostAndDivine.Server.Repositories;
using LostAndDivine.Server.Services;
using LostAndDivine.Shared.Commands;
using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;
using System.Text.Json;

namespace LostAndDivine.Server;

public class AuthService
{
    private readonly GameServices _svc;

    public AuthService(IGameServices svc)
    {
        _svc = (GameServices)svc;
    }

    public async Task<bool> HandleAuthMessage(ClientConnection connection, GameMessage message, INetworkHub hub)
    {
        switch (message.Type)
        {
            case GameMessageType.Register:
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
                            Type = GameMessageType.AuthResponse,
                            Data = new { Success = true, Message = "Регистрация успешна! Теперь войдите в аккаунт." }
                        });
                        Log.Info($"Зарегистрирован новый аккаунт: {account.Login}");
                        return false;
                    }
                    else
                    {
                        await hub.SendToClient(connection, new GameMessage
                        {
                            Type = GameMessageType.AuthResponse,
                            Data = new { Success = false, Message = "Ошибка регистрации. Логин уже занят." }
                        });
                    }
                }
                break;

            case GameMessageType.LoginAuth:
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
                                Type = GameMessageType.AuthResponse,
                                Data = new { Success = false, Message = $"Вы заблокированы. Причина: {account.BanReason}" }
                            });
                            Log.Info($"Заблокированный игрок пытался войти: {account.Login}");
                            return false;
                        }

                        var characters = CharacterRepository.ListForAccount(account.Login);
                        var sessionToken = SessionManager.CreateToken(account.Login);

                        connection.AuthenticatedLogin = account.Login;
                        connection.SessionToken = sessionToken;
                        connection.IsAdmin = account.IsAdmin;

                        await hub.SendToClient(connection, new GameMessage
                        {
                            Type = GameMessageType.AuthResponse,
                            Data = new
                            {
                                Success = true,
                                Message = "Авторизация успешна",
                                session_token = sessionToken,
                                login = account.Login,
                                characters = characters.Select(c => new
                                {
                                    name = c.Name,
                                    level = c.Level,
                                    className = c.Class,
                                    zone = c.Zone
                                }).ToArray()
                            }
                        });

                        Log.Info($"Аккаунт {account.Login} авторизован. {characters.Count} персонажей.");
                        return false;
                    }
                    else
                    {
                        await hub.SendToClient(connection, new GameMessage
                        {
                            Type = GameMessageType.AuthResponse,
                            Data = new { Success = false, Message = "Неверный логин или пароль!" }
                        });
                    }
                }
                break;

            case GameMessageType.CharacterSelect:
                return await HandleCharacterSelect(connection, message, hub);

            case GameMessageType.CharacterCreate:
                return await HandleCharacterCreate(connection, message, hub);

            case GameMessageType.CharacterDelete:
                await HandleCharacterDelete(connection, message, hub);
                break;
        }

        return false;
    }

    public async Task<bool> SpawnPlayer(ClientConnection connection, string characterName)
    {
        var ch = CharacterRepository.LoadByName(characterName);
        if (ch == null)
            return false;

        var existingSession = _svc.World.GetConnectionByPlayerName(ch.Name);
        if (existingSession != null)
        {
            await _svc.Hub.SendToClient(connection, new GameMessage
            {
                Type = GameMessageType.AuthResponse,
                Data = new { Success = false, Message = "Этот персонаж уже в игре." }
            });
            return false;
        }

        var player = PlayerFactory.FromCharacter(ch, _svc);
        player.IsAdmin = connection.IsAdmin;

        _svc.World.AddPlayer(player);
        connection.Player = player;
        connection.LastPongReceived = DateTime.UtcNow;

        var playerSessionToken = SessionManager.CreateToken(player.Name);
        connection.SessionToken = playerSessionToken;

        await _svc.Hub.SendToClient(connection, new GameMessage
        {
            Type = GameMessageType.AuthResponse,
            Data = new { Success = true, Message = $"Добро пожаловать, {player.Name}!", session_token = playerSessionToken, player_id = player.Id }
        });

        await _svc.Hub.SendToClient(connection, new GameMessage
        {
                            Type = GameMessageType.Welcome,
            Data = new { Message = $"Добро пожаловать, {player.Name}!", PlayerName = player.Name, ClassName = player.Class.DisplayName() }
        });

        await _svc.ClientBuild.SendChangelogAsync(connection, _svc.Hub);

        connection.WelcomeSent = true;

        Log.Info($"Игрок {player.Name} вошел в мир на позиции ({player.X}, {player.Y})");
        await _svc.Hub.BroadcastMapAsync();
        await _svc.Hub.SendQuestLog(connection, player);
        var granted = _svc.Quests.TryAutoGrant(player, player.CurrentZoneId);
        foreach (var d in granted)
            await _svc.Hub.SendChatToAsync(connection, ChatChannel.System, "Система", $"Новое задание: {d.Title}");
        if (granted.Count > 0)
            await _svc.Hub.SendQuestLog(connection, player);
        await _svc.Hub.SendHotbar(connection, player);
        await _svc.Hub.SendInventoryAndStatus(connection, player);

        int unreadCount = MailRepository.CountUnread(player.Name);
        await _svc.Hub.SendToClient(connection, new GameMessage
        {
                            Type = GameMessageType.MailUnread,
            Data = new { Count = unreadCount }
        });

        return true;
    }

    private async Task<bool> HandleCharacterSelect(ClientConnection connection, GameMessage message, INetworkHub hub)
    {
        var json = JsonSerializer.Serialize(message.Data);
        var el = JsonDocument.Parse(json).RootElement;
        string? name = el.TryGetProperty("Name", out var n) ? n.GetString() : null;

        if (string.IsNullOrWhiteSpace(name))
        {
            await hub.SendToClient(connection, new GameMessage
            {
                            Type = GameMessageType.CharacterList,
                Data = new { Error = "Имя персонажа не указано" }
            });
            return false;
        }

        var ch = CharacterRepository.LoadByName(name);
        if (ch == null || ch.AccountLogin != connection.AuthenticatedLogin)
        {
            await hub.SendToClient(connection, new GameMessage
            {
                            Type = GameMessageType.CharacterList,
                Data = new { Error = "Персонаж не найден" }
            });
            return false;
        }

        return await SpawnPlayer(connection, name);
    }

    private async Task<bool> HandleCharacterCreate(ClientConnection connection, GameMessage message, INetworkHub hub)
    {
        var json = JsonSerializer.Serialize(message.Data);
        var el = JsonDocument.Parse(json).RootElement;
        string? name = el.TryGetProperty("Name", out var n) ? n.GetString() : null;
        int classVal = el.TryGetProperty("Class", out var c) ? c.GetInt32() : 0;

        if (string.IsNullOrWhiteSpace(name) || name.Length < 3 || name.Length > 20)
        {
            await hub.SendToClient(connection, new GameMessage
            {
                            Type = GameMessageType.CharacterList,
                Data = new { Error = "Имя должно быть от 3 до 20 символов" }
            });
            return false;
        }

        if (!name.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9'))
        {
            await hub.SendToClient(connection, new GameMessage
            {
                            Type = GameMessageType.CharacterList,
                Data = new { Error = "Имя: только латинские буквы и цифры" }
            });
            return false;
        }

        if (CharacterRepository.NameTaken(name))
        {
            await hub.SendToClient(connection, new GameMessage
            {
                            Type = GameMessageType.CharacterList,
                Data = new { Error = "Имя уже занято" }
            });
            return false;
        }

        if (connection.AuthenticatedLogin == null)
        {
            await hub.SendToClient(connection, new GameMessage
            {
                            Type = GameMessageType.CharacterList,
                Data = new { Error = "Не авторизован" }
            });
            return false;
        }

        var cls = (CharacterClass)classVal;
        var ch = CharacterRepository.Create(connection.AuthenticatedLogin, name, cls);

        var characters = CharacterRepository.ListForAccount(connection.AuthenticatedLogin);
        await hub.SendToClient(connection, new GameMessage
        {
            Type = GameMessageType.CharacterList,
            Data = new
            {
                Created = true,
                Name = ch.Name,
                characters = characters.Select(c2 => new
                {
                    name = c2.Name,
                    level = c2.Level,
                    className = c2.Class,
                    zone = c2.Zone
                }).ToArray()
            }
        });

        return await SpawnPlayer(connection, name);
    }

    private async Task HandleCharacterDelete(ClientConnection connection, GameMessage message, INetworkHub hub)
    {
        var json = JsonSerializer.Serialize(message.Data);
        var el = JsonDocument.Parse(json).RootElement;
        string? name = el.TryGetProperty("Name", out var n) ? n.GetString() : null;

        if (string.IsNullOrWhiteSpace(name) || connection.AuthenticatedLogin == null)
        {
            await hub.SendToClient(connection, new GameMessage
            {
                            Type = GameMessageType.CharacterList,
                Data = new { Error = "Неверный запрос" }
            });
            return;
        }

        var ch = CharacterRepository.LoadByName(name);
        if (ch == null || ch.AccountLogin != connection.AuthenticatedLogin)
        {
            await hub.SendToClient(connection, new GameMessage
            {
                            Type = GameMessageType.CharacterList,
                Data = new { Error = "Персонаж не найден" }
            });
            return;
        }

        CharacterRepository.DeleteCharacter(name);

        var characters = CharacterRepository.ListForAccount(connection.AuthenticatedLogin);
        await hub.SendToClient(connection, new GameMessage
        {
            Type = GameMessageType.CharacterList,
            Data = new
            {
                Deleted = true,
                Name = name,
                characters = characters.Select(c2 => new
                {
                    name = c2.Name,
                    level = c2.Level,
                    className = c2.Class,
                    zone = c2.Zone
                }).ToArray()
            }
        });
    }
}

using RPGGame.Server.Network;
using RPGGame.Server.Services;
using RPGGame.Shared.Commands;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;
using System.Text.Json;

namespace RPGGame.Server;

public partial class Program
{
    private static async Task<bool> HandleAuthMessage(ClientConnection connection, GameMessage message, INetworkHub hub)
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

                        int spawnX, spawnY;
                        if (account.PlayerData.X >= 0 && account.PlayerData.Y >= 0)
                        {
                            spawnX = account.PlayerData.X;
                            spawnY = account.PlayerData.Y;
                        }
                        else
                        {
                            spawnX = MerchantManager.MerchantX + World.NextRandom(Balance.RespawnJitterMin, Balance.RespawnJitterMax);
                            spawnY = MerchantManager.MerchantY + World.NextRandom(Balance.RespawnJitterMin, Balance.RespawnJitterMax);
                            spawnX = Math.Clamp(spawnX, 0, World.Map.Width - 1);
                            spawnY = Math.Clamp(spawnY, 0, World.Map.Height - 1);
                        }

                        var player = new Player
                        {
                            Name = account.PlayerName,
                            X = spawnX,
                            Y = spawnY,
                            Level = account.PlayerData.Level,
                            Experience = account.PlayerData.Experience,
                            Health = account.PlayerData.Health,
                            MaxHealth = account.PlayerData.MaxHealth,
                            Attack = account.PlayerData.Attack,
                            Defense = account.PlayerData.Defense,
                            Gold = account.PlayerData.Gold,
                            Strength = account.PlayerData.Strength,
                            Stamina = account.PlayerData.Stamina,
                            Agility = account.PlayerData.Agility,
                            Cunning = account.PlayerData.Cunning,
                            Wisdom = account.PlayerData.Wisdom,
                            Will = account.PlayerData.Will,
                            AttributePoints = account.PlayerData.AttributePoints,
                            Speed = account.PlayerData.Speed,
                            Inventory = account.PlayerData.Inventory,
                            Equipment = account.PlayerData.Equipment,
                            ActiveQuests = account.PlayerData.ActiveQuests,
                            HotbarSlots = account.PlayerData.HotbarSlots,
                            MaxMana = Balance.MaxMana(account.PlayerData.Will),
                            IsAdmin = account.IsAdmin
                        };
                        player.Mana = player.MaxMana;

                        // Тестовый аккаунт: очень высокая скорость перемещения
                        if (player.Name.Equals("test", StringComparison.OrdinalIgnoreCase)
                            || player.Name.Equals("тест", StringComparison.OrdinalIgnoreCase))
                            player.Speed = 50;

                        World.AddPlayer(player);
                        connection.Player = player;

                        // Create reconnect token
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

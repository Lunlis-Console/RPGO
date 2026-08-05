using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using RPGGame.Shared.Models;
using RPGGame.Server.Repositories;

namespace RPGGame.Server.Repositories;

internal static class AccountRepository
{
    internal static string HashPassword(string password)
    {
        using var sha256 = SHA256.Create();
        byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
        return Convert.ToBase64String(bytes);
    }

    internal static int GetCount()
    {
        lock (Db.Lock)
        {
            using var connection = Db.Open();
            var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM accounts";
            return (int)(long)cmd.ExecuteScalar()!;
        }
    }

    internal static (bool Success, Account? Account) Register(string login, string password, string playerName)
    {
        lock (Db.Lock)
        {
            using var connection = Db.Open();

            var checkLogin = connection.CreateCommand();
            checkLogin.CommandText = "SELECT COUNT(*) FROM accounts WHERE login = $login";
            checkLogin.Parameters.AddWithValue("$login", login);
            if ((long)checkLogin.ExecuteScalar()! > 0)
                return (false, null);

            var checkName = connection.CreateCommand();
            checkName.CommandText = "SELECT COUNT(*) FROM accounts WHERE player_name = $name";
            checkName.Parameters.AddWithValue("$name", playerName);
            if ((long)checkName.ExecuteScalar()! > 0)
                return (false, null);

            var account = new Account
            {
                Login = login,
                PasswordHash = HashPassword(password),
                PlayerName = playerName,
                PlayerData = new PlayerData(),
                CreatedAt = DateTime.Now,
                LastLogin = DateTime.Now
            };

            Insert(connection, account);
            return (true, account);
        }
    }

    internal static (bool Success, Account? Account) Login(string login, string password)
    {
        lock (Db.Lock)
        {
            using var connection = Db.Open();

            var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT password_hash, player_name FROM accounts WHERE login = $login";
            cmd.Parameters.AddWithValue("$login", login);

            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
                return (false, null);

            string storedHash = reader.GetString(0);
            reader.Close();

            if (storedHash != HashPassword(password))
                return (false, null);

            var updateLogin = connection.CreateCommand();
            updateLogin.CommandText = "UPDATE accounts SET last_login = $now WHERE login = $login";
            updateLogin.Parameters.AddWithValue("$now", DateTime.Now.ToString("o"));
            updateLogin.Parameters.AddWithValue("$login", login);
            updateLogin.ExecuteNonQuery();

            var account = LoadFull(connection, login);

            if (account != null && account.IsBanned)
                return (false, null);

            return (true, account);
        }
    }

    internal static void CreateTestAccountIfNeeded()
    {
        lock (Db.Lock)
        {
            using var connection = Db.Open();

            var check = connection.CreateCommand();
            check.CommandText = "SELECT COUNT(*) FROM accounts WHERE login = 'test'";
            long exists = (long)check.ExecuteScalar()!;

            if (exists > 0)
            {
                var promote = connection.CreateCommand();
                promote.CommandText = "UPDATE accounts SET is_admin = 1 WHERE login = 'test' AND is_admin = 0";
                promote.ExecuteNonQuery();
                return;
            }

            var testAccount = new Account
            {
                Login = "test",
                PasswordHash = HashPassword("123"),
                PlayerName = "Тест",
                PlayerData = new PlayerData
                {
                    Level = 5,
                    Experience = 100,
                    Health = 150,
                    MaxHealth = 150,
                    Gold = 500,
                    Inventory = new List<Item>
                    {
                        new Item { Name = "Меч новобранца", TemplateId = "W0002", Type = "weapon", Value = 30, BonusPhysAttack = 0, DamageMin = 15, DamageMax = 15, Description = "Качество: Обычный", MaxStack = Balance.UniqueItemMaxStack },
                        new Item { Name = "Зелье здоровья", Type = "consumable", Value = 20, HealAmount = 50, Description = "Восстанавливает 50 HP", MaxStack = Balance.DefaultMaxStack }
                    }
                }
            };

            Insert(connection, testAccount);
            Log.Info("Создан тестовый аккаунт: test / 123");
        }
    }

    internal static void SavePlayerProgress(Player player, List<Item>? storageItems = null)
    {
        lock (Db.Lock)
        {
            using var connection = Db.Open();
            using var txn = connection.BeginTransaction();

            var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                UPDATE accounts SET
                    level = $level,
                    experience = $exp,
                    health = $hp,
                    max_health = $maxhp,
                    mana = $mana,
                    gold = $gold,
                    strength = $str,
                    endurance = $end,
                    agility = $agi,
                    cunning = $cun,
                    intellect = $intel,
                    wisdom = $wis,
                    attribute_points = $ap,
                    skill_points = $sp,
                    learned_skills = $ls,
                    skill_ranks = $sr,
                    speed = $spd,
                    pos_x = $posx,
                    pos_y = $posy,
                    hotbar_slots = $hotbar,
                    current_zone = $zone
                WHERE player_name = $name";

            cmd.Parameters.AddWithValue("$level", player.Level);
            cmd.Parameters.AddWithValue("$exp", player.Experience);
            cmd.Parameters.AddWithValue("$hp", player.Health);
            cmd.Parameters.AddWithValue("$maxhp", player.MaxHealth);
            cmd.Parameters.AddWithValue("$mana", player.Mana);
            cmd.Parameters.AddWithValue("$gold", player.Gold);
            cmd.Parameters.AddWithValue("$str", player.Strength);
            cmd.Parameters.AddWithValue("$end", player.Endurance);
            cmd.Parameters.AddWithValue("$agi", player.Agility);
            cmd.Parameters.AddWithValue("$cun", player.Cunning);
            cmd.Parameters.AddWithValue("$intel", player.Intellect);
            cmd.Parameters.AddWithValue("$wis", player.Wisdom);
            cmd.Parameters.AddWithValue("$ap", player.AttributePoints);
            cmd.Parameters.AddWithValue("$sp", player.SkillPoints);
            cmd.Parameters.AddWithValue("$ls", System.Text.Json.JsonSerializer.Serialize(player.LearnedSkills));
            cmd.Parameters.AddWithValue("$sr", System.Text.Json.JsonSerializer.Serialize(player.SkillRanks));
            cmd.Parameters.AddWithValue("$spd", player.Speed);
            cmd.Parameters.AddWithValue("$posx", player.X);
            cmd.Parameters.AddWithValue("$posy", player.Y);
            cmd.Parameters.AddWithValue("$name", player.Name);
            cmd.Parameters.AddWithValue("$hotbar", System.Text.Json.JsonSerializer.Serialize(player.HotbarSlots));
            cmd.Parameters.AddWithValue("$zone", player.CurrentZoneId);
            cmd.ExecuteNonQuery();

            InventoryRepository.SaveEquipment(connection, player.Name, player.Equipment);

            var deleteItems = connection.CreateCommand();
            deleteItems.CommandText = "DELETE FROM inventory WHERE player_name = $name";
            deleteItems.Parameters.AddWithValue("$name", player.Name);
            deleteItems.ExecuteNonQuery();

            foreach (var item in player.Inventory)
            {
                InventoryRepository.InsertItem(connection, player.Name, item);
            }

            QuestRepository.Save(connection, player.Name, player.ActiveQuests);
            QuestRepository.SaveCompleted(connection, player.Name, player.CompletedQuestIds);

            if (storageItems != null)
                StorageRepository.SaveAll(connection, player.Name, storageItems);

            txn.Commit();
        }
    }

    internal static void SetAdmin(string login, bool isAdmin)
    {
        lock (Db.Lock)
        {
            using var conn = Db.Open();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE accounts SET is_admin = $val WHERE login = $login";
            cmd.Parameters.AddWithValue("$val", isAdmin ? 1 : 0);
            cmd.Parameters.AddWithValue("$login", login);
            cmd.ExecuteNonQuery();
        }
    }

    internal static void SetBanned(string login, bool isBanned, string reason)
    {
        lock (Db.Lock)
        {
            using var conn = Db.Open();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE accounts SET is_banned = $val, ban_reason = $reason WHERE login = $login";
            cmd.Parameters.AddWithValue("$val", isBanned ? 1 : 0);
            cmd.Parameters.AddWithValue("$reason", reason);
            cmd.Parameters.AddWithValue("$login", login);
            cmd.ExecuteNonQuery();
        }
    }

    internal static string? GetLoginByPlayerName(string playerName)
    {
        using var conn = Db.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT login FROM accounts WHERE player_name = $name";
        cmd.Parameters.AddWithValue("$name", playerName);
        return cmd.ExecuteScalar() as string;
    }

    private static void Insert(SqliteConnection connection, Account account)
    {
        using var transaction = connection.BeginTransaction();

        var insertAccount = connection.CreateCommand();
        insertAccount.CommandText = @"
            INSERT OR IGNORE INTO accounts (login, password_hash, player_name, level, experience,
                health, max_health, gold, created_at, last_login,
                strength, endurance, agility, cunning, intellect, wisdom, attribute_points, speed, is_admin)
            VALUES ($login, $hash, $name, $level, $exp, $hp, $maxhp, $gold, $created, $last,
                $str, $end, $agi, $cun, $intel, $wis, $ap, $spd, $admin)";

        insertAccount.Parameters.AddWithValue("$login", account.Login);
        insertAccount.Parameters.AddWithValue("$hash", account.PasswordHash);
        insertAccount.Parameters.AddWithValue("$name", account.PlayerName);
        insertAccount.Parameters.AddWithValue("$level", account.PlayerData.Level);
        insertAccount.Parameters.AddWithValue("$exp", account.PlayerData.Experience);
        insertAccount.Parameters.AddWithValue("$hp", account.PlayerData.Health);
        insertAccount.Parameters.AddWithValue("$maxhp", account.PlayerData.MaxHealth);
        insertAccount.Parameters.AddWithValue("$gold", account.PlayerData.Gold);
        insertAccount.Parameters.AddWithValue("$created", account.CreatedAt.ToString("o"));
        insertAccount.Parameters.AddWithValue("$last", account.LastLogin.ToString("o"));
        insertAccount.Parameters.AddWithValue("$str", account.PlayerData.Strength);
        insertAccount.Parameters.AddWithValue("$end", account.PlayerData.Endurance);
        insertAccount.Parameters.AddWithValue("$agi", account.PlayerData.Agility);
        insertAccount.Parameters.AddWithValue("$cun", account.PlayerData.Cunning);
        insertAccount.Parameters.AddWithValue("$intel", account.PlayerData.Intellect);
        insertAccount.Parameters.AddWithValue("$wis", account.PlayerData.Wisdom);
        insertAccount.Parameters.AddWithValue("$ap", account.PlayerData.AttributePoints);
        insertAccount.Parameters.AddWithValue("$spd", account.PlayerData.Speed);
        insertAccount.Parameters.AddWithValue("$admin", account.IsAdmin ? 1 : 0);
        insertAccount.ExecuteNonQuery();

        InventoryRepository.SaveEquipment(connection, account.PlayerName, account.PlayerData.Equipment);

        foreach (var item in account.PlayerData.Inventory)
        {
            InventoryRepository.InsertItem(connection, account.PlayerName, item);
        }

        transaction.Commit();
    }

    /// <summary>
    /// Загружает аккаунт по имени игрока (нужно для reconnect после перезапуска,
    /// когда игрока ещё нет в мире).
    /// </summary>
    internal static Account? LoadByPlayerName(string playerName)
    {
        using var conn = Db.Open();
        var loginCmd = conn.CreateCommand();
        loginCmd.CommandText = "SELECT login FROM accounts WHERE player_name = $name";
        loginCmd.Parameters.AddWithValue("$name", playerName);
        var login = loginCmd.ExecuteScalar() as string;
        if (login == null) return null;

        return LoadFull(conn, login);
    }

    internal static Account? LoadFull(SqliteConnection connection, string login)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT player_name, password_hash, level, experience, health, max_health, mana,
                   gold, created_at, last_login,
                   strength, endurance, agility, cunning, intellect, wisdom, attribute_points, speed, pos_x, pos_y,
                   hotbar_slots, is_admin, is_banned, ban_reason, skill_points, learned_skills, skill_ranks, current_zone
            FROM accounts WHERE login = $login";
        cmd.Parameters.AddWithValue("$login", login);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        string playerName = reader.GetString(0);
        var equipIds = InventoryRepository.GetEquipmentIds(connection, playerName);

        var account = new Account
        {
            Login = login,
            PasswordHash = reader.GetString(1),
            PlayerName = playerName,
            CreatedAt = DateTime.Parse(reader.GetString(8)),
            LastLogin = DateTime.Parse(reader.GetString(9)),
            IsAdmin = !reader.IsDBNull(21) && reader.GetInt32(21) != 0,
            IsBanned = !reader.IsDBNull(22) && reader.GetInt32(22) != 0,
            BanReason = reader.IsDBNull(23) ? "" : reader.GetString(23),
            PlayerData = new PlayerData
            {
                Level = reader.GetInt32(2),
                Experience = reader.GetInt32(3),
                Health = reader.GetInt32(4),
                MaxHealth = reader.GetInt32(5),
                Mana = reader.GetInt32(6),
                Gold = reader.GetInt32(7),
                Strength = reader.GetInt32(10),
                Endurance = reader.GetInt32(11),
                Agility = reader.GetInt32(12),
                Cunning = reader.GetInt32(13),
                Intellect = reader.GetInt32(14),
                Wisdom = reader.GetInt32(15),
                AttributePoints = reader.GetInt32(16),
                Speed = reader.GetInt32(17),
                X = reader.GetInt32(18),
                Y = reader.GetInt32(19),
                SkillPoints = reader.GetInt32(24),
                LearnedSkills = ParseStringList(reader.IsDBNull(25) ? "[]" : reader.GetString(25)),
                SkillRanks = ParseSkillRanks(reader.IsDBNull(26) ? "{}" : reader.GetString(26)),
                CurrentZoneId = reader.IsDBNull(27) ? Balance.MainZoneId : reader.GetString(27),
                Inventory = InventoryRepository.GetForPlayer(playerName, equipIds),
                Equipment = InventoryRepository.LoadEquipment(connection, playerName),
                ActiveQuests = QuestRepository.Load(connection, playerName),
                CompletedQuestIds = QuestRepository.LoadCompleted(connection, playerName),
                HotbarSlots = LoadHotbar(reader.GetString(20))
            }
        };

        return account;
    }

    private static List<string?> LoadHotbar(string json)
    {
        var default10 = new List<string?>(10) { null, null, null, null, null, null, null, null, null, null };
        if (string.IsNullOrWhiteSpace(json)) return default10;
        try
        {
            var list = System.Text.Json.JsonSerializer.Deserialize<List<string?>>(json);
            if (list == null || list.Count == 0) return default10;
            while (list.Count < 10) list.Add(null);
            return list.Take(10).ToList();
        }
        catch { return default10; }
    }

    private static List<string> ParseStringList(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? new(); }
        catch { return new(); }
    }

    private static Dictionary<string, int> ParseSkillRanks(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, int>>(json) ?? new(); }
        catch { return new(); }
    }
}

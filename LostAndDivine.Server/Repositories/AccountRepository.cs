using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using LostAndDivine.Shared.Models;
using LostAndDivine.Server.Repositories;

namespace LostAndDivine.Server.Repositories;

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

    internal static (bool Success, Account? Account) Register(string login, string password)
    {
        if (string.IsNullOrWhiteSpace(login) || login.Length < 3 || login.Length > 20)
            return (false, null);
        if (!login.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9'))
            return (false, null);
        if (string.IsNullOrWhiteSpace(password) || password.Length < 6 || password.Length > 50)
            return (false, null);
        if (!password.Any(char.IsUpper))
            return (false, null);
        if (!password.Any(c => !char.IsLetterOrDigit(c)))
            return (false, null);

        lock (Db.Lock)
        {
            using var connection = Db.Open();

            var checkLogin = connection.CreateCommand();
            checkLogin.CommandText = "SELECT COUNT(*) FROM accounts WHERE login = $login";
            checkLogin.Parameters.AddWithValue("$login", login);
            if ((long)checkLogin.ExecuteScalar()! > 0)
                return (false, null);

            var account = new Account
            {
                Login = login,
                PasswordHash = HashPassword(password),
                CreatedAt = DateTime.Now,
                LastLogin = DateTime.Now
            };

            Insert(connection, account);
            return (true, account);
        }
    }

    internal static (bool Success, Account? Account) Login(string login, string password)
    {
        if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(password))
            return (false, null);

        lock (Db.Lock)
        {
            using var connection = Db.Open();

            var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT password_hash FROM accounts WHERE login = $login";
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

                // Ensure test character exists after migration
                var checkChar = connection.CreateCommand();
                checkChar.CommandText = "SELECT COUNT(*) FROM characters WHERE player_name = 'Тест'";
                long charExists = (long)checkChar.ExecuteScalar()!;
                if (charExists == 0)
                {
                    var warriorClass = CharacterClass.Warrior;
                    CreateTestCharacter(connection, warriorClass);
                }
                return;
            }

            var testAccount = new Account
            {
                Login = "test",
                PasswordHash = HashPassword("123"),
                CreatedAt = DateTime.Now,
                LastLogin = DateTime.Now,
                IsAdmin = true
            };

            Insert(connection, testAccount);
            CreateTestCharacter(connection, CharacterClass.Warrior);
            Log.Info("Создан тестовый аккаунт: test / 123");
        }
    }

    private static void CreateTestCharacter(SqliteConnection connection, CharacterClass cls)
    {
        var (s, e, a, c, i, w) = cls.BaseStats();
        var ch = new CharacterModel
        {
            Name = "Тест",
            AccountLogin = "test",
            Class = cls,
            Level = 5,
            Experience = 100,
            Health = 150,
            MaxHealth = 150,
            Gold = 500,
            Strength = s,
            Endurance = e,
            Agility = a,
            Cunning = c,
            Intellect = i,
            Wisdom = w,
            Speed = 5,
            Inventory = new List<Item>
            {
                new Item { Name = "Меч новобранца", TemplateId = "W0002", Type = "weapon", Value = 30, DamageMin = 15, DamageMax = 15, Description = "Качество: Обычный", MaxStack = Balance.UniqueItemMaxStack },
                new Item { Name = "Зелье здоровья", Type = "consumable", Value = 20, HealAmount = 50, Description = "Восстанавливает 50 HP", MaxStack = Balance.DefaultMaxStack }
            },
            CreatedAt = DateTime.Now
        };

        var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO characters (player_name, account_login, class, level, experience, health, max_health, mana, gold,
                strength, endurance, agility, cunning, intellect, wisdom, attribute_points, skill_points,
                learned_skills, skill_ranks, speed, pos_x, pos_y, hotbar_slots, current_zone, created_at, last_login)
            VALUES ($name, $login, $class, $level, $exp, $hp, $maxhp, 100, $gold,
                $str, $end, $agi, $cun, $intel, $wis, 0, 0,
                '[]', '{}', $spd, -1, -1, '[]', 'main', $created, $last)";
        cmd.Parameters.AddWithValue("$name", ch.Name);
        cmd.Parameters.AddWithValue("$login", ch.AccountLogin);
        cmd.Parameters.AddWithValue("$class", (int)ch.Class);
        cmd.Parameters.AddWithValue("$level", ch.Level);
        cmd.Parameters.AddWithValue("$exp", ch.Experience);
        cmd.Parameters.AddWithValue("$hp", ch.Health);
        cmd.Parameters.AddWithValue("$maxhp", ch.MaxHealth);
        cmd.Parameters.AddWithValue("$gold", ch.Gold);
        cmd.Parameters.AddWithValue("$str", ch.Strength);
        cmd.Parameters.AddWithValue("$end", ch.Endurance);
        cmd.Parameters.AddWithValue("$agi", ch.Agility);
        cmd.Parameters.AddWithValue("$cun", ch.Cunning);
        cmd.Parameters.AddWithValue("$intel", ch.Intellect);
        cmd.Parameters.AddWithValue("$wis", ch.Wisdom);
        cmd.Parameters.AddWithValue("$spd", ch.Speed);
        cmd.Parameters.AddWithValue("$created", ch.CreatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("$last", DateTime.Now.ToString("o"));
        cmd.ExecuteNonQuery();

        InventoryRepository.SaveEquipment(connection, ch.Name, ch.Equipment);
        foreach (var item in ch.Inventory)
            InventoryRepository.InsertItem(connection, ch.Name, item);
    }

    internal static void SavePlayerProgress(Player player, List<Item>? storageItems = null)
    {
        CharacterRepository.SavePlayerProgress(player, storageItems);
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
        cmd.CommandText = "SELECT account_login FROM characters WHERE player_name = $name";
        cmd.Parameters.AddWithValue("$name", playerName);
        return cmd.ExecuteScalar() as string;
    }

    private static void Insert(SqliteConnection connection, Account account)
    {
        var insertAccount = connection.CreateCommand();
        insertAccount.CommandText = @"
            INSERT INTO accounts (login, password_hash, player_name, created_at, last_login, is_admin)
            VALUES ($login, $hash, $name, $created, $last, $admin)";

        insertAccount.Parameters.AddWithValue("$login", account.Login);
        insertAccount.Parameters.AddWithValue("$hash", account.PasswordHash);
        insertAccount.Parameters.AddWithValue("$name", account.Login);
        insertAccount.Parameters.AddWithValue("$created", account.CreatedAt.ToString("o"));
        insertAccount.Parameters.AddWithValue("$last", account.LastLogin.ToString("o"));
        insertAccount.Parameters.AddWithValue("$admin", account.IsAdmin ? 1 : 0);
        insertAccount.ExecuteNonQuery();
    }

    internal static Account? LoadByPlayerName(string playerName)
    {
        // Find login by character name, then load account
        using var conn = Db.Open();
        var loginCmd = conn.CreateCommand();
        loginCmd.CommandText = "SELECT account_login FROM characters WHERE player_name = $name";
        loginCmd.Parameters.AddWithValue("$name", playerName);
        var login = loginCmd.ExecuteScalar() as string;
        if (login == null) return null;

        return LoadFull(conn, login);
    }

    internal static Account? LoadFull(SqliteConnection connection, string login)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT login, password_hash, created_at, is_admin, is_banned, ban_reason
            FROM accounts WHERE login = $login";
        cmd.Parameters.AddWithValue("$login", login);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        return new Account
        {
            Login = reader.GetString(0),
            PasswordHash = reader.GetString(1),
            CreatedAt = DateTime.Parse(reader.GetString(2)),
            IsAdmin = !reader.IsDBNull(3) && reader.GetInt32(3) != 0,
            IsBanned = !reader.IsDBNull(4) && reader.GetInt32(4) != 0,
            BanReason = reader.IsDBNull(5) ? "" : reader.GetString(5)
        };
    }
}

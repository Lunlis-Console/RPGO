using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using RPGGame.Shared.Models;
using RPGGame.Shared.Migrations;

namespace RPGGame.Server;

public static class DatabaseManager
{
    private static readonly string _dbFile = ResolveDbPath();
    private static readonly string _connectionString = $"Data Source={_dbFile}";
    private static readonly string _oldAccountsFile = "accounts.json";
    private static readonly object _lock = new();

    // Ищем game.db: сначала в папке запуска, затем поднимаемся вверх к корню решения,
    // чтобы сервер и редактор использовали ОДНУ И ТУ ЖЕ базу.
    private static string ResolveDbPath()
    {
        var candidates = new List<string>();
        string? baseDir = AppContext.BaseDirectory;
        candidates.Add(Path.Combine(baseDir, "game.db"));
        for (int i = 0; i < 6; i++)
        {
            baseDir = Path.GetDirectoryName(baseDir);
            if (baseDir == null) break;
            candidates.Add(Path.Combine(baseDir, "game.db"));
        }
        foreach (var c in candidates)
        {
            if (File.Exists(c)) return Path.GetFullPath(c);
        }
        // Если файла нет — создаём рядом с .csproj (корень проекта)
        string? root = AppContext.BaseDirectory;
        for (int i = 0; i < 6; i++)
        {
            root = Path.GetDirectoryName(root);
            if (root == null) break;
            if (File.Exists(Path.Combine(root, "RPGO.Server.csproj")))
                return Path.Combine(root, "game.db");
        }
        return "game.db";
    }

    public static void Initialize()
    {
        DbMigrationRunner.RunMigrations(_connectionString);
        Log.Info("База данных инициализирована");

        MigrateFromJsonIfNeeded();
    }

    private static void MigrateFromJsonIfNeeded()
    {
        if (!File.Exists(_oldAccountsFile)) return;

        try
        {
            Log.Info("Найден accounts.json, перенос в SQLite...");
            string json = File.ReadAllText(_oldAccountsFile, Encoding.UTF8);
            var accounts = JsonSerializer.Deserialize<List<Account>>(json) ?? new List<Account>();

            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            foreach (var account in accounts)
            {
                InsertAccount(connection, account);
            }

            File.Move(_oldAccountsFile, _oldAccountsFile + ".bak");
            Log.Info($"Перенесено {accounts.Count} аккаунтов. Файл переименован в accounts.json.bak");
        }
        catch (Exception ex)
        {
            Log.Error($"Ошибка миграции: {ex.Message}", ex);
        }
    }

    public static void SaveNpcs(SqliteConnection connection, string id, string name, string type, int x, int y, string? data)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO npcs (id, name, type, x, y, data) VALUES ($id,$n,$t,$x,$y,$d)
            ON CONFLICT(id) DO UPDATE SET name=$n, type=$t, x=$x, y=$y, data=$d";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$n", name);
        cmd.Parameters.AddWithValue("$t", type);
        cmd.Parameters.AddWithValue("$x", x);
        cmd.Parameters.AddWithValue("$y", y);
        cmd.Parameters.AddWithValue("$d", (object?)data ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public class NpcRecord
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public int X { get; set; }
        public int Y { get; set; }
        public string? Data { get; set; }
    }

    public static List<NpcRecord> LoadNpcs()
    {
        var list = new List<NpcRecord>();
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, type, x, y, data FROM npcs ORDER BY id";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new NpcRecord
            {
                Id = reader.GetString(0),
                Name = reader.GetString(1),
                Type = reader.GetString(2),
                X = reader.GetInt32(3),
                Y = reader.GetInt32(4),
                Data = reader.IsDBNull(5) ? null : reader.GetString(5),
            });
        }
        return list;
    }

    public static void SaveNpcs(List<NpcRecord> npcs)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var transaction = conn.BeginTransaction();
        using (var del = conn.CreateCommand())
        {
            del.CommandText = "DELETE FROM npcs";
            del.ExecuteNonQuery();
        }
        foreach (var n in npcs)
        {
            UpsertNpc(conn, n.Id, n.Name, n.Type, n.X, n.Y, n.Data);
        }
        transaction.Commit();
    }

    private static void UpsertNpc(SqliteConnection connection, string id, string name, string type, int x, int y, string? data)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO npcs (id, name, type, x, y, data) VALUES ($id,$n,$t,$x,$y,$d)
            ON CONFLICT(id) DO UPDATE SET name=$n, type=$t, x=$x, y=$y, data=$d";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$n", name);
        cmd.Parameters.AddWithValue("$t", type);
        cmd.Parameters.AddWithValue("$x", x);
        cmd.Parameters.AddWithValue("$y", y);
        cmd.Parameters.AddWithValue("$d", (object?)data ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private static int CountRows(SqliteConnection connection, string table)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {table}";
        return (int)(long)cmd.ExecuteScalar()!;
    }

    public static int GetWorldConfigInt(string key, int defaultValue = 0)
    {
        lock (_lock)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT value FROM world_config WHERE key = $k";
                cmd.Parameters.AddWithValue("$k", key);
                var v = cmd.ExecuteScalar();
                return v == null ? defaultValue : (int)(long)v;
            }
            catch
            {
                return defaultValue;
            }
        }
    }

    public static List<Item> LoadItems()
    {
        lock (_lock)
        {
            var result = new List<Item>();
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            var cmd = connection.CreateCommand();
            cmd.CommandText = @"SELECT id, name, type, value, attack, defense, max_health_bonus, heal_amount, stock, description,
                bonus_strength, bonus_stamina, bonus_agility, bonus_cunning, bonus_wisdom, bonus_will, bonus_crit_chance, bonus_crit_damage, bonus_evade_chance
                FROM items";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new Item
                {
                    Id = reader.GetString(0),
                    TemplateId = reader.GetString(0),
                    Name = reader.GetString(1),
                    Type = reader.GetString(2),
                    Value = reader.GetInt32(3),
                    Attack = reader.GetInt32(4),
                    Defense = reader.GetInt32(5),
                    MaxHealthBonus = reader.GetInt32(6),
                    HealAmount = reader.GetInt32(7),
                    Stock = reader.GetInt32(8),
                    Description = reader.GetString(9),
                    BonusStrength = reader.GetInt32(10),
                    BonusStamina = reader.GetInt32(11),
                    BonusAgility = reader.GetInt32(12),
                    BonusCunning = reader.GetInt32(13),
                    BonusWisdom = reader.GetInt32(14),
                    BonusWill = reader.GetInt32(15),
                    BonusCritChance = reader.GetDouble(16),
                    BonusCritDamage = reader.GetDouble(17),
                    BonusEvadeChance = reader.GetDouble(18),
                    MaxStack = Balance.MaxStackForType(reader.GetString(2)),
                });
            }
            return result;
        }
    }

    // ===== Ассортимент торговца =====
    public static List<string> LoadMerchantStock(string npcId)
    {
        lock (_lock)
        {
            var result = new List<string>();
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT item_id FROM merchant_stock WHERE npc_id = $npc ORDER BY item_id";
            cmd.Parameters.AddWithValue("$npc", npcId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                result.Add(reader.GetString(0));
            return result;
        }
    }

    public static void SaveMerchantStock(string npcId, IEnumerable<string> itemIds)
    {
        lock (_lock)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var transaction = connection.BeginTransaction();
            using (var del = connection.CreateCommand())
            {
                del.CommandText = "DELETE FROM merchant_stock WHERE npc_id = $npc";
                del.Parameters.AddWithValue("$npc", npcId);
                del.ExecuteNonQuery();
            }
            foreach (var itemId in itemIds)
            {
                if (string.IsNullOrWhiteSpace(itemId)) continue;
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "INSERT OR IGNORE INTO merchant_stock (npc_id, item_id) VALUES ($npc, $item)";
                cmd.Parameters.AddWithValue("$npc", npcId);
                cmd.Parameters.AddWithValue("$item", itemId);
                cmd.ExecuteNonQuery();
            }
            transaction.Commit();
        }
    }

    public class MonsterTemplate
    {
        public string Id = "";
        public string Name = "";
        public int Tier;
        public int Health;
        public int Attack;
        public int Defense;
        public int XpReward;
        public int GoldReward;
        public char Symbol = 'M';
        public int Strength = 1;
        public int Stamina = 1;
        public int Agility = 1;
        public int Cunning = 1;
        public int Wisdom = 1;
        public int Will = 1;
        public double CritChance = 1.0;
        public double CritDamage = 1.5;
        public double EvadeChance = 1.0;
    }

    public static List<MonsterTemplate> LoadMonsterTemplates()
    {
        lock (_lock)
        {
            var result = new List<MonsterTemplate>();
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT id, name, tier, health, attack, defense, xp_reward, gold_reward, symbol, strength, stamina, agility, cunning, wisdom, will, crit_chance, crit_damage, evade_chance FROM monsters";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new MonsterTemplate
                {
                    Id = reader.GetString(0),
                    Name = reader.GetString(1),
                    Tier = reader.GetInt32(2),
                    Health = reader.GetInt32(3),
                    Attack = reader.GetInt32(4),
                    Defense = reader.GetInt32(5),
                    XpReward = reader.GetInt32(6),
                    GoldReward = reader.GetInt32(7),
                    Symbol = reader.GetString(8).Length > 0 ? reader.GetString(8)[0] : 'M',
                    Strength = reader.GetInt32(9),
                    Stamina = reader.GetInt32(10),
                    Agility = reader.GetInt32(11),
                    Cunning = reader.GetInt32(12),
                    Wisdom = reader.GetInt32(13),
                    Will = reader.GetInt32(14),
                    CritChance = reader.GetDouble(15),
                    CritDamage = reader.GetDouble(16),
                    EvadeChance = reader.GetDouble(17),
                });
            }
            return result;
        }
    }

    public class LootEntry
    {
        public int Id;
        public string MonsterId = "";
        public string Name = "";
        public string Description = "";
        public int Value;
        public int DropChance;
    }

    public static List<LootEntry> LoadLootTable()
    {
        lock (_lock)
        {
            var result = new List<LootEntry>();
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT id, monster_id, name, description, value, drop_chance FROM loot_tables ORDER BY monster_id, id";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new LootEntry
                {
                    Id = reader.GetInt32(0),
                    MonsterId = reader.GetString(1),
                    Name = reader.GetString(2),
                    Description = reader.GetString(3),
                    Value = reader.GetInt32(4),
                    DropChance = reader.GetInt32(5),
                });
            }
            return result;
        }
    }

    public static List<QuestDefinition> LoadQuestDefinitions()
    {
        lock (_lock)
        {
            var result = new List<QuestDefinition>();
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT id, title, description, type, target_monster_id, target_item_id, target, xp_reward, gold_reward FROM quests_def";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new QuestDefinition
                {
                    Id = reader.GetString(0),
                    Title = reader.GetString(1),
                    Description = reader.GetString(2),
                    Type = reader.GetString(3),
                    TargetMonsterId = reader.GetString(4),
                    TargetItemId = reader.GetString(5),
                    Target = reader.GetInt32(6),
                    XpReward = reader.GetInt32(7),
                    GoldReward = reader.GetInt32(8),
                });
            }
            return result;
        }
    }

    private static void InsertAccount(SqliteConnection connection, Account account)
    {
        using var transaction = connection.BeginTransaction();

        var insertAccount = connection.CreateCommand();
        insertAccount.CommandText = @"
            INSERT OR IGNORE INTO accounts (login, password_hash, player_name, level, experience,
                health, max_health, attack, defense, gold, created_at, last_login,
                strength, stamina, agility, cunning, wisdom, will_val, attribute_points, speed)
            VALUES ($login, $hash, $name, $level, $exp, $hp, $maxhp, $atk, $def, $gold, $created, $last,
                $str, $sta, $agi, $cun, $wis, $wil, $ap, $spd)";

        insertAccount.Parameters.AddWithValue("$login", account.Login);
        insertAccount.Parameters.AddWithValue("$hash", account.PasswordHash);
        insertAccount.Parameters.AddWithValue("$name", account.PlayerName);
        insertAccount.Parameters.AddWithValue("$level", account.PlayerData.Level);
        insertAccount.Parameters.AddWithValue("$exp", account.PlayerData.Experience);
        insertAccount.Parameters.AddWithValue("$hp", account.PlayerData.Health);
        insertAccount.Parameters.AddWithValue("$maxhp", account.PlayerData.MaxHealth);
        insertAccount.Parameters.AddWithValue("$atk", account.PlayerData.Attack);
        insertAccount.Parameters.AddWithValue("$def", account.PlayerData.Defense);
        insertAccount.Parameters.AddWithValue("$gold", account.PlayerData.Gold);
        insertAccount.Parameters.AddWithValue("$created", account.CreatedAt.ToString("o"));
        insertAccount.Parameters.AddWithValue("$last", account.LastLogin.ToString("o"));
        insertAccount.Parameters.AddWithValue("$str", account.PlayerData.Strength);
        insertAccount.Parameters.AddWithValue("$sta", account.PlayerData.Stamina);
        insertAccount.Parameters.AddWithValue("$agi", account.PlayerData.Agility);
        insertAccount.Parameters.AddWithValue("$cun", account.PlayerData.Cunning);
        insertAccount.Parameters.AddWithValue("$wis", account.PlayerData.Wisdom);
        insertAccount.Parameters.AddWithValue("$wil", account.PlayerData.Will);
        insertAccount.Parameters.AddWithValue("$ap", account.PlayerData.AttributePoints);
        insertAccount.Parameters.AddWithValue("$spd", account.PlayerData.Speed);
        insertAccount.ExecuteNonQuery();

        SaveEquipment(connection, account.PlayerName, account.PlayerData.Equipment);

        foreach (var item in account.PlayerData.Inventory)
        {
            SaveInventoryItem(connection, account.PlayerName, item);
        }

        transaction.Commit();
    }

    private static void SaveInventoryItem(SqliteConnection connection, string playerName, Item item)
    {
        InsertInventoryItem(connection, playerName, item);
    }

    public static void CreateTestAccountIfNeeded()
    {
        lock (_lock)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var check = connection.CreateCommand();
            check.CommandText = "SELECT COUNT(*) FROM accounts WHERE login = 'test'";
            long exists = (long)check.ExecuteScalar()!;

            if (exists > 0) return;

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
                    Attack = 25,
                    Defense = 15,
                    Gold = 500,
                    Inventory = new List<Item>
                    {
                        new Item { Name = "Железный меч", Type = "weapon", Value = 5, Attack = 2, Description = "Надёжный железный меч", MaxStack = Balance.UniqueItemMaxStack },
                        new Item { Name = "Зелье здоровья", Type = "consumable", Value = 20, HealAmount = 50, Description = "Восстанавливает 50 HP", MaxStack = Balance.DefaultMaxStack }
                    }
                }
            };

            InsertAccount(connection, testAccount);
            Log.Info("Создан тестовый аккаунт: test / 123");
        }
    }

    public static string HashPassword(string password)
    {
        using var sha256 = SHA256.Create();
        byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
        return Convert.ToBase64String(bytes);
    }

    public static (bool Success, Account? Account) Register(string login, string password, string playerName)
    {
        lock (_lock)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

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

            InsertAccount(connection, account);
            return (true, account);
        }
    }

    public static (bool Success, Account? Account) Login(string login, string password)
    {
        lock (_lock)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT password_hash, player_name FROM accounts WHERE login = $login";
            cmd.Parameters.AddWithValue("$login", login);

            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
                return (false, null);

            string storedHash = reader.GetString(0);
            string playerName = reader.GetString(1);
            reader.Close();

            if (storedHash != HashPassword(password))
                return (false, null);

            var updateLogin = connection.CreateCommand();
            updateLogin.CommandText = "UPDATE accounts SET last_login = $now WHERE login = $login";
            updateLogin.Parameters.AddWithValue("$now", DateTime.Now.ToString("o"));
            updateLogin.Parameters.AddWithValue("$login", login);
            updateLogin.ExecuteNonQuery();

            var account = LoadFullAccount(connection, login);
            return (true, account);
        }
    }

    public static void SavePlayerProgress(Player player)
    {
        lock (_lock)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                UPDATE accounts SET
                    level = $level,
                    experience = $exp,
                    health = $hp,
                    max_health = $maxhp,
                    attack = $atk,
                    defense = $def,
                    gold = $gold,
                    strength = $str,
                    stamina = $sta,
                    agility = $agi,
                    cunning = $cun,
                    wisdom = $wis,
                    will_val = $wil,
                    attribute_points = $ap,
                    speed = $spd,
                    pos_x = $posx,
                    pos_y = $posy,
                    hotbar_slots = $hotbar
                WHERE player_name = $name";

            cmd.Parameters.AddWithValue("$level", player.Level);
            cmd.Parameters.AddWithValue("$exp", player.Experience);
            cmd.Parameters.AddWithValue("$hp", player.Health);
            cmd.Parameters.AddWithValue("$maxhp", player.MaxHealth);
            cmd.Parameters.AddWithValue("$atk", player.Attack);
            cmd.Parameters.AddWithValue("$def", player.Defense);
            cmd.Parameters.AddWithValue("$gold", player.Gold);
            cmd.Parameters.AddWithValue("$str", player.Strength);
            cmd.Parameters.AddWithValue("$sta", player.Stamina);
            cmd.Parameters.AddWithValue("$agi", player.Agility);
            cmd.Parameters.AddWithValue("$cun", player.Cunning);
            cmd.Parameters.AddWithValue("$wis", player.Wisdom);
            cmd.Parameters.AddWithValue("$wil", player.Will);
            cmd.Parameters.AddWithValue("$ap", player.AttributePoints);
            cmd.Parameters.AddWithValue("$spd", player.Speed);
            cmd.Parameters.AddWithValue("$posx", player.X);
            cmd.Parameters.AddWithValue("$posy", player.Y);
            cmd.Parameters.AddWithValue("$name", player.Name);
            cmd.Parameters.AddWithValue("$hotbar", System.Text.Json.JsonSerializer.Serialize(player.HotbarSlots));
            cmd.ExecuteNonQuery();

            SaveEquipment(connection, player.Name, player.Equipment);

            var deleteItems = connection.CreateCommand();
            deleteItems.CommandText = "DELETE FROM inventory WHERE player_name = $name";
            deleteItems.Parameters.AddWithValue("$name", player.Name);
            deleteItems.ExecuteNonQuery();

            foreach (var item in player.Inventory)
            {
                InsertInventoryItem(connection, player.Name, item);
            }

            SaveQuests(connection, player.Name, player.ActiveQuests);
        }
    }

    private static void SaveQuests(SqliteConnection connection, string playerName, List<QuestProgress> quests)
    {
        var delete = connection.CreateCommand();
        delete.CommandText = "DELETE FROM quests WHERE player_name = $name";
        delete.Parameters.AddWithValue("$name", playerName);
        delete.ExecuteNonQuery();

        foreach (var q in quests)
        {
            var insert = connection.CreateCommand();
            insert.CommandText = @"
                INSERT INTO quests (player_name, quest_id, current, completed)
                VALUES ($name, $qid, $cur, $comp)";
            insert.Parameters.AddWithValue("$name", playerName);
            insert.Parameters.AddWithValue("$qid", q.QuestId);
            insert.Parameters.AddWithValue("$cur", q.Current);
            insert.Parameters.AddWithValue("$comp", q.Completed ? 1 : 0);
            insert.ExecuteNonQuery();
        }
    }

    private static List<QuestProgress> LoadQuests(SqliteConnection connection, string playerName)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT quest_id, current, completed FROM quests WHERE player_name = $name";
        cmd.Parameters.AddWithValue("$name", playerName);

        var list = new List<QuestProgress>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new QuestProgress
            {
                QuestId = reader.GetString(0),
                Current = reader.GetInt32(1),
                Completed = reader.GetInt32(2) != 0
            });
        }
        return list;
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

    public static int GetAccountCount()
    {
        lock (_lock)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM accounts";
            return (int)(long)cmd.ExecuteScalar()!;
        }
    }

    public static List<Item> GetInventory(string playerName)
    {
        return GetInventory(playerName, null);
    }

    private static void InsertInventoryItem(SqliteConnection connection, string playerName, Item item)
    {
        int qty = Math.Max(1, item.Quantity);

        if (!string.IsNullOrEmpty(item.TemplateId) && Balance.MaxStackForType(item.Type) > 1)
        {
            var find = connection.CreateCommand();
            find.CommandText = @"SELECT id, quantity FROM inventory
                WHERE player_name = $name AND COALESCE(template_id,'') = $tid
                ORDER BY quantity DESC LIMIT 1";
            find.Parameters.AddWithValue("$name", playerName);
            find.Parameters.AddWithValue("$tid", item.TemplateId);
            using var reader = find.ExecuteReader();
            if (reader.Read())
            {
                string existingId = reader.GetString(0);
                int existingQty = reader.GetInt32(1);
                int room = Math.Max(0, Balance.MaxStackForType(item.Type) - existingQty);
                if (room > 0)
                {
                    int add = Math.Min(room, qty);
                    var upd = connection.CreateCommand();
                    upd.CommandText = "UPDATE inventory SET quantity = quantity + $q WHERE id = $id";
                    upd.Parameters.AddWithValue("$q", add);
                    upd.Parameters.AddWithValue("$id", existingId);
                    upd.ExecuteNonQuery();
                    qty -= add;
                }
            }
        }

        if (qty <= 0) return;

        var insertItem = connection.CreateCommand();
        insertItem.CommandText = @"
            INSERT INTO inventory (player_name, item_id, name, type, value, attack, defense, max_health_bonus, heal_amount, description,
                bonus_strength, bonus_stamina, bonus_agility, bonus_cunning, bonus_wisdom, bonus_will, bonus_crit_chance, bonus_crit_damage, bonus_evade_chance, template_id, quantity)
            VALUES ($name, $itemid, $iname, $itype, $val, $atk, $def, $mhp, $heal, $desc,
                $str, $sta, $agi, $cun, $wis, $wil, $cc, $cd, $ec, $tid, $qty)";
        insertItem.Parameters.AddWithValue("$name", playerName);
        insertItem.Parameters.AddWithValue("$itemid", item.Id);
        insertItem.Parameters.AddWithValue("$iname", item.Name);
        insertItem.Parameters.AddWithValue("$itype", item.Type);
        insertItem.Parameters.AddWithValue("$val", item.Value);
        insertItem.Parameters.AddWithValue("$atk", item.Attack);
        insertItem.Parameters.AddWithValue("$def", item.Defense);
        insertItem.Parameters.AddWithValue("$mhp", item.MaxHealthBonus);
        insertItem.Parameters.AddWithValue("$heal", item.HealAmount);
        insertItem.Parameters.AddWithValue("$desc", item.Description);
        insertItem.Parameters.AddWithValue("$str", item.BonusStrength);
        insertItem.Parameters.AddWithValue("$sta", item.BonusStamina);
        insertItem.Parameters.AddWithValue("$agi", item.BonusAgility);
        insertItem.Parameters.AddWithValue("$cun", item.BonusCunning);
        insertItem.Parameters.AddWithValue("$wis", item.BonusWisdom);
        insertItem.Parameters.AddWithValue("$wil", item.BonusWill);
        insertItem.Parameters.AddWithValue("$cc", item.BonusCritChance);
        insertItem.Parameters.AddWithValue("$cd", item.BonusCritDamage);
        insertItem.Parameters.AddWithValue("$ec", item.BonusEvadeChance);
        insertItem.Parameters.AddWithValue("$tid", item.TemplateId);
        insertItem.Parameters.AddWithValue("$qty", qty);
        insertItem.ExecuteNonQuery();
    }

    public static List<Item> GetInventory(string playerName, HashSet<string>? excludeItemIds)
    {
        lock (_lock)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var cmd = connection.CreateCommand();
            cmd.CommandText = @"SELECT item_id, name, type, value, attack, defense, max_health_bonus, heal_amount, description,
                bonus_strength, bonus_stamina, bonus_agility, bonus_cunning, bonus_wisdom, bonus_will, bonus_crit_chance, bonus_crit_damage, bonus_evade_chance, template_id, quantity
                FROM inventory WHERE player_name = $name";
            cmd.Parameters.AddWithValue("$name", playerName);

            var items = new List<Item>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                string itemId = reader.GetString(0);
                if (excludeItemIds != null && excludeItemIds.Contains(itemId))
                    continue;
                    
                items.Add(new Item
                {
                    Id = itemId,
                    Name = reader.GetString(1),
                    Type = reader.GetString(2),
                    Value = reader.GetInt32(3),
                    Attack = reader.GetInt32(4),
                    Defense = reader.GetInt32(5),
                    MaxHealthBonus = reader.GetInt32(6),
                    HealAmount = reader.GetInt32(7),
                    Description = reader.GetString(8),
                    BonusStrength = reader.GetInt32(9),
                    BonusStamina = reader.GetInt32(10),
                    BonusAgility = reader.GetInt32(11),
                    BonusCunning = reader.GetInt32(12),
                    BonusWisdom = reader.GetInt32(13),
                    BonusWill = reader.GetInt32(14),
                    BonusCritChance = reader.GetDouble(15),
                    BonusCritDamage = reader.GetDouble(16),
                    BonusEvadeChance = reader.GetDouble(17),
                    TemplateId = reader.IsDBNull(18) ? "" : reader.GetString(18),
                    Quantity = reader.IsDBNull(19) ? 1 : reader.GetInt32(19)
                });
            }

            var result = new List<Item>();
            foreach (var item in items)
            {
                SyncItemFromTemplate(connection, item);
                item.MaxStack = Balance.MaxStackForType(item.Type);

                if (item.MaxStack <= 1 && item.Quantity > 1)
                {
                    // Предметы экипировки не стакаются: разбиваем стек на отдельные записи
                    for (int k = 0; k < item.Quantity; k++)
                    {
                        result.Add(new Item
                        {
                            Id = Guid.NewGuid().ToString(),
                            TemplateId = item.TemplateId,
                            Name = item.Name,
                            Type = item.Type,
                            Value = item.Value,
                            Attack = item.Attack,
                            Defense = item.Defense,
                            MaxHealthBonus = item.MaxHealthBonus,
                            HealAmount = item.HealAmount,
                            Description = item.Description,
                            MaxStack = item.MaxStack,
                            Quantity = 1,
                            BonusStrength = item.BonusStrength,
                            BonusStamina = item.BonusStamina,
                            BonusAgility = item.BonusAgility,
                            BonusCunning = item.BonusCunning,
                            BonusWisdom = item.BonusWisdom,
                            BonusWill = item.BonusWill,
                            BonusCritChance = item.BonusCritChance,
                            BonusCritDamage = item.BonusCritDamage,
                            BonusEvadeChance = item.BonusEvadeChance,
                            TwoHanded = item.TwoHanded
                        });
                    }
                }
                else
                {
                    result.Add(item);
                }
            }

            return result;
        }
    }

    private static HashSet<string> GetEquipmentIds(SqliteConnection connection, string playerName)
    {
        var ids = new HashSet<string>();
        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT item_id FROM player_equipment WHERE player_name = $name";
        cmd.Parameters.AddWithValue("$name", playerName);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (!reader.IsDBNull(0)) ids.Add(reader.GetString(0));
        }
        return ids;
    }

    private static void SaveEquipment(SqliteConnection connection, string playerName, Equipment equipment)
    {
        using (var del = connection.CreateCommand())
        {
            del.CommandText = "DELETE FROM player_equipment WHERE player_name = $name";
            del.Parameters.AddWithValue("$name", playerName);
            del.ExecuteNonQuery();
        }

        foreach (var kv in equipment.Slots)
        {
            if (kv.Value == null) continue;
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "INSERT INTO player_equipment (player_name, slot, item_id, item_data) VALUES ($name, $slot, $id, $data)";
            cmd.Parameters.AddWithValue("$name", playerName);
            cmd.Parameters.AddWithValue("$slot", kv.Key);
            cmd.Parameters.AddWithValue("$id", kv.Value.Id);
            cmd.Parameters.AddWithValue("$data", System.Text.Json.JsonSerializer.Serialize(kv.Value));
            cmd.ExecuteNonQuery();
        }
    }

    private static Equipment LoadEquipment(SqliteConnection connection, string playerName)
    {
        var equipment = new Equipment();

        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT slot, item_id, item_data FROM player_equipment WHERE player_name = $name";
        cmd.Parameters.AddWithValue("$name", playerName);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            string slot = reader.IsDBNull(0) ? "" : reader.GetString(0);
            if (string.IsNullOrEmpty(slot)) continue;

            // Полный предмет хранится прямо в player_equipment
            if (!reader.IsDBNull(2))
            {
                var json = reader.GetString(2);
                var item = System.Text.Json.JsonSerializer.Deserialize<Item>(json);
                if (item != null) { equipment[slot] = item; continue; }
            }

            // Старые записи без item_data — пробуем найти в инвентаре по item_id
            string itemId = reader.IsDBNull(1) ? "" : reader.GetString(1);
            if (!string.IsNullOrEmpty(itemId))
            {
                var item = FindInventoryItem(connection, playerName, itemId);
                if (item != null) equipment[slot] = item;
            }
        }

        return equipment;
    }

    private static Item? FindInventoryItem(SqliteConnection connection, string playerName, string itemId)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = @"SELECT item_id, name, type, value, attack, defense, max_health_bonus, heal_amount, description,
            bonus_strength, bonus_stamina, bonus_agility, bonus_cunning, bonus_wisdom, bonus_will, bonus_crit_chance, bonus_crit_damage, bonus_evade_chance, template_id, quantity
            FROM inventory WHERE player_name = $name AND item_id = $id";
        cmd.Parameters.AddWithValue("$name", playerName);
        cmd.Parameters.AddWithValue("$id", itemId);

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            var item = new Item
            {
                Id = reader.GetString(0),
                Name = reader.GetString(1),
                Type = reader.GetString(2),
                Value = reader.GetInt32(3),
                Attack = reader.GetInt32(4),
                Defense = reader.GetInt32(5),
                MaxHealthBonus = reader.GetInt32(6),
                HealAmount = reader.GetInt32(7),
                Description = reader.GetString(8),
                BonusStrength = reader.GetInt32(9),
                BonusStamina = reader.GetInt32(10),
                BonusAgility = reader.GetInt32(11),
                BonusCunning = reader.GetInt32(12),
                BonusWisdom = reader.GetInt32(13),
                BonusWill = reader.GetInt32(14),
                BonusCritChance = reader.GetDouble(15),
                BonusCritDamage = reader.GetDouble(16),
                BonusEvadeChance = reader.GetDouble(17),
                TemplateId = reader.IsDBNull(18) ? "" : reader.GetString(18),
                Quantity = reader.IsDBNull(19) ? 1 : reader.GetInt32(19)
            };
            return SyncItemFromTemplate(connection, item);
        }
        return null;
    }

    private static Item SyncItemFromTemplate(SqliteConnection connection, Item item)
    {
        if (string.IsNullOrEmpty(item.TemplateId)) return item;
        var cmd = connection.CreateCommand();
        cmd.CommandText = @"SELECT attack, defense, value, max_health_bonus, heal_amount, description,
            bonus_strength, bonus_stamina, bonus_agility, bonus_cunning, bonus_wisdom, bonus_will,
            bonus_crit_chance, bonus_crit_damage, bonus_evade_chance
            FROM items WHERE id = $tid";
        cmd.Parameters.AddWithValue("$tid", item.TemplateId);
        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            item.Attack = reader.GetInt32(0);
            item.Defense = reader.GetInt32(1);
            item.Value = reader.GetInt32(2);
            item.MaxHealthBonus = reader.GetInt32(3);
            item.HealAmount = reader.GetInt32(4);
            item.Description = reader.GetString(5);
            item.BonusStrength = reader.GetInt32(6);
            item.BonusStamina = reader.GetInt32(7);
            item.BonusAgility = reader.GetInt32(8);
            item.BonusCunning = reader.GetInt32(9);
            item.BonusWisdom = reader.GetInt32(10);
            item.BonusWill = reader.GetInt32(11);
            item.BonusCritChance = reader.GetDouble(12);
            item.BonusCritDamage = reader.GetDouble(13);
            item.BonusEvadeChance = reader.GetDouble(14);
        }
        return item;
    }

    private static Account? LoadFullAccount(SqliteConnection connection, string login)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT player_name, password_hash, level, experience, health, max_health,
                   attack, defense, gold, created_at, last_login,
                   strength, stamina, agility, cunning, wisdom, will_val, attribute_points, speed, pos_x, pos_y,
                   hotbar_slots
            FROM accounts WHERE login = $login";
        cmd.Parameters.AddWithValue("$login", login);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        string playerName = reader.GetString(0);
        var equipIds = GetEquipmentIds(connection, playerName);

        var account = new Account
        {
            Login = login,
            PasswordHash = reader.GetString(1),
            PlayerName = playerName,
            CreatedAt = DateTime.Parse(reader.GetString(9)),
            LastLogin = DateTime.Parse(reader.GetString(10)),
            PlayerData = new PlayerData
            {
                Level = reader.GetInt32(2),
                Experience = reader.GetInt32(3),
                Health = reader.GetInt32(4),
                MaxHealth = reader.GetInt32(5),
                Attack = reader.GetInt32(6),
                Defense = reader.GetInt32(7),
                Gold = reader.GetInt32(8),
                Strength = reader.GetInt32(11),
                Stamina = reader.GetInt32(12),
                Agility = reader.GetInt32(13),
                Cunning = reader.GetInt32(14),
                Wisdom = reader.GetInt32(15),
                Will = reader.GetInt32(16),
                AttributePoints = reader.GetInt32(17),
                Speed = reader.GetInt32(18),
                X = reader.GetInt32(19),
                Y = reader.GetInt32(20),
                Inventory = GetInventory(playerName, equipIds),
                Equipment = LoadEquipment(connection, playerName),
                ActiveQuests = LoadQuests(connection, playerName),
                HotbarSlots = LoadHotbar(reader.GetString(21))
            }
        };

        return account;
    }

    private static List<Skill>? _skillsCache;

    public static List<Skill> LoadSkills()
    {
        if (_skillsCache != null)
            return _skillsCache;

        var result = new List<Skill>();
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        var cmd = connection.CreateCommand();
        cmd.CommandText = @"SELECT id, name, description, type, mp_cost, cooldown_ms, damage_multiplier, min_level
            FROM skills";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new Skill
            {
                Id = reader.GetString(0),
                Name = reader.GetString(1),
                Description = reader.GetString(2),
                Type = reader.GetString(3),
                MpCost = reader.GetInt32(4),
                CooldownMs = reader.GetInt32(5),
                DamageMultiplier = reader.GetDouble(6),
                MinLevel = reader.GetInt32(7)
            });
        }
        _skillsCache = result;
        return result;
    }

    public static Skill? GetSkill(string id)
        => LoadSkills().FirstOrDefault(s => s.Id == id);

    // ----- Друзья -----

    public static void AddFriend(string ownerName, string friendName)
    {
        lock (_lock)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT OR IGNORE INTO friends (owner_name, friend_name, created_at)
                VALUES ($owner, $friend, $now)";
            cmd.Parameters.AddWithValue("$owner", ownerName);
            cmd.Parameters.AddWithValue("$friend", friendName);
            cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
            cmd.ExecuteNonQuery();
        }
    }

    public static void RemoveFriend(string ownerName, string friendName)
    {
        lock (_lock)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                DELETE FROM friends WHERE owner_name = $owner AND friend_name = $friend";
            cmd.Parameters.AddWithValue("$owner", ownerName);
            cmd.Parameters.AddWithValue("$friend", friendName);
            cmd.ExecuteNonQuery();
        }
    }

    public static List<string> GetFriendNames(string ownerName)
    {
        var names = new List<string>();
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT friend_name FROM friends WHERE owner_name = $owner ORDER BY friend_name";
        cmd.Parameters.AddWithValue("$owner", ownerName);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            names.Add(reader.GetString(0));
        return names;
    }

    public static bool FriendExists(string ownerName, string friendName)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT 1 FROM friends WHERE owner_name = $owner AND friend_name = $friend LIMIT 1";
        cmd.Parameters.AddWithValue("$owner", ownerName);
        cmd.Parameters.AddWithValue("$friend", friendName);
        return cmd.ExecuteScalar() != null;
    }

    /// <summary>Возвращает имена игроков, у которых ownerName есть в друзьях (обратные ссылки).</summary>
    public static List<string> GetReverseFriendNames(string ownerName)
    {
        var names = new List<string>();
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT owner_name FROM friends WHERE friend_name = $owner";
        cmd.Parameters.AddWithValue("$owner", ownerName);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            names.Add(reader.GetString(0));
        return names;
    }

    /// <summary>Существует ли зарегистрированный персонаж с таким именем.</summary>
    public static bool PlayerNameExists(string playerName)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM accounts WHERE player_name = $name LIMIT 1";
        cmd.Parameters.AddWithValue("$name", playerName);
        return cmd.ExecuteScalar() != null;
    }

    /// <summary>Максимальное число друзей у одного игрока (как в классических ММО).</summary>
    public const int MaxFriends = 50;

}

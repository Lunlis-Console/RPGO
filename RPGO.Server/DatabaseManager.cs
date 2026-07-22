using Microsoft.Data.Sqlite;
using RPGGame.Shared.Models;
using RPGGame.Shared.Migrations;
using RPGGame.Server.Repositories;

namespace RPGGame.Server;

/// <summary>
/// Thin static facade over repository classes.
/// Preserves backward compatibility: all existing callers (DatabaseManager.XXX) keep working.
/// </summary>
public static class DatabaseManager
{
    // === Lifecycle ===
    public static void Initialize()
    {
        DbMigrationRunner.RunMigrations(Db.ConnectionString);
        Log.Info("База данных инициализирована");
        MigrateFromJsonIfNeeded();
    }

    private static void MigrateFromJsonIfNeeded()
    {
        var oldFile = "accounts.json";
        if (!File.Exists(oldFile)) return;

        try
        {
            Log.Info("Найден accounts.json, перенос в SQLite...");
            string json = File.ReadAllText(oldFile, System.Text.Encoding.UTF8);
            var accounts = System.Text.Json.JsonSerializer.Deserialize<List<Account>>(json) ?? new List<Account>();

            using var conn = Db.Open();
            foreach (var account in accounts)
            {
                var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT OR IGNORE INTO accounts (login, password_hash, player_name, level, experience,
                        health, max_health, gold, created_at, last_login,
                        strength, endurance, agility, cunning, intellect, wisdom, attribute_points, speed, is_admin)
                    VALUES ($login, $hash, $name, $level, $exp, $hp, $maxhp, $gold, $created, $last,
                        $str, $end, $agi, $cun, $intel, $wis, $ap, $spd, $admin)";
                cmd.Parameters.AddWithValue("$login", account.Login);
                cmd.Parameters.AddWithValue("$hash", account.PasswordHash);
                cmd.Parameters.AddWithValue("$name", account.PlayerName);
                cmd.Parameters.AddWithValue("$level", account.PlayerData.Level);
                cmd.Parameters.AddWithValue("$exp", account.PlayerData.Experience);
                cmd.Parameters.AddWithValue("$hp", account.PlayerData.Health);
                cmd.Parameters.AddWithValue("$maxhp", account.PlayerData.MaxHealth);
                cmd.Parameters.AddWithValue("$gold", account.PlayerData.Gold);
                cmd.Parameters.AddWithValue("$created", account.CreatedAt.ToString("o"));
                cmd.Parameters.AddWithValue("$last", account.LastLogin.ToString("o"));
                cmd.Parameters.AddWithValue("$str", account.PlayerData.Strength);
                cmd.Parameters.AddWithValue("$end", account.PlayerData.Endurance);
                cmd.Parameters.AddWithValue("$agi", account.PlayerData.Agility);
                cmd.Parameters.AddWithValue("$cun", account.PlayerData.Cunning);
                cmd.Parameters.AddWithValue("$intel", account.PlayerData.Intellect);
                cmd.Parameters.AddWithValue("$wis", account.PlayerData.Wisdom);
                cmd.Parameters.AddWithValue("$ap", account.PlayerData.AttributePoints);
                cmd.Parameters.AddWithValue("$spd", account.PlayerData.Speed);
                cmd.Parameters.AddWithValue("$admin", account.IsAdmin ? 1 : 0);
                cmd.ExecuteNonQuery();
            }

            File.Move(oldFile, oldFile + ".bak");
            Log.Info($"Перенесено {accounts.Count} аккаунтов. Файл переименован в accounts.json.bak");
        }
        catch (Exception ex)
        {
            Log.Error($"Ошибка миграции: {ex.Message}", ex);
        }
    }

    // === Account ===
    public static void CreateTestAccountIfNeeded() => AccountRepository.CreateTestAccountIfNeeded();
    public static string HashPassword(string password) => AccountRepository.HashPassword(password);
    public static int GetAccountCount() => AccountRepository.GetCount();
    public static (bool Success, Account? Account) Register(string login, string password, string playerName) => AccountRepository.Register(login, password, playerName);
    public static (bool Success, Account? Account) Login(string login, string password) => AccountRepository.Login(login, password);
    public static void SavePlayerProgress(Player player) => AccountRepository.SavePlayerProgress(player);
    public static void SetAdmin(string login, bool isAdmin) => AccountRepository.SetAdmin(login, isAdmin);
    public static void SetBanned(string login, bool isBanned, string reason) => AccountRepository.SetBanned(login, isBanned, reason);
    public static string? GetLoginByPlayerName(string playerName) => AccountRepository.GetLoginByPlayerName(playerName);

    // === Inventory ===
    public static List<Item> GetInventory(string playerName) => InventoryRepository.GetForPlayer(playerName);
    public static List<Item> GetInventory(string playerName, HashSet<string>? excludeItemIds) => InventoryRepository.GetForPlayer(playerName, excludeItemIds);

    // === Items ===
    public static List<Item> LoadItems() => ItemRepository.LoadAll();
    public static Item? GetItemTemplate(string templateId) => ItemRepository.GetTemplate(templateId);

    // === Monsters ===
    public static List<MonsterTemplate> LoadMonsterTemplates() => MonsterRepository.LoadAll();

    // === Loot ===
    public static List<LootEntry> LoadLootTable() => LootRepository.LoadAll();

    // === Quests ===
    public static List<QuestDefinition> LoadQuestDefinitions() => QuestRepository.LoadDefinitions();

    // === NPCs ===
    public static void SaveNpcs(SqliteConnection connection, string id, string name, string type, int x, int y, string? data) => NpcRepository.SaveSingle(id, name, type, x, y, data);
    public static List<NpcRecord> LoadNpcs() => NpcRepository.LoadAll();
    public static void SaveNpcs(List<NpcRecord> npcs) => NpcRepository.SaveAll(npcs);

    // === Merchants ===
    public static List<string> LoadMerchantStock(string npcId) => MerchantRepository.LoadStock(npcId);
    public static void SaveMerchantStock(string npcId, IEnumerable<string> itemIds) => MerchantRepository.SaveStock(npcId, itemIds);

    // === World Config ===
    public static int GetWorldConfigInt(string key, int defaultValue = 0) => WorldConfigRepository.GetInt(key, defaultValue);

    // === Skills ===
    public static List<Skill> LoadSkills() => SkillRepository.LoadAll();
    public static Skill? GetSkill(string id) => SkillRepository.GetById(id);

    // === Friends ===
    public static void AddFriend(string ownerName, string friendName) => FriendRepository.Add(ownerName, friendName);
    public static void RemoveFriend(string ownerName, string friendName) => FriendRepository.Remove(ownerName, friendName);
    public static List<string> GetFriendNames(string ownerName) => FriendRepository.GetNames(ownerName);
    public static bool FriendExists(string ownerName, string friendName) => FriendRepository.Exists(ownerName, friendName);
    public static List<string> GetReverseFriendNames(string ownerName) => FriendRepository.GetReverseNames(ownerName);
    public static bool PlayerNameExists(string playerName) => FriendRepository.PlayerNameExists(playerName);
    public const int MaxFriends = FriendRepository.MaxFriends;
}

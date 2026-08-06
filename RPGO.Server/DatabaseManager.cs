using Microsoft.Data.Sqlite;
using RPGGame.Shared;
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

        bool contentExisted = File.Exists(Db.ContentPath);
        DbMigrationRunner.RunMigrations(Db.ContentConnectionString);
        if (!contentExisted)
            ContentDbSeeder.CopyContentFromRuntimeIfNew(Db.ContentConnectionString, Db.RuntimePath);

        Log.Info("База данных инициализирована");
    }

    // === Account ===
    public static void CreateTestAccountIfNeeded() => AccountRepository.CreateTestAccountIfNeeded();
    public static string HashPassword(string password) => AccountRepository.HashPassword(password);
    public static int GetAccountCount() => AccountRepository.GetCount();
    public static (bool Success, Account? Account) Register(string login, string password, string playerName) => AccountRepository.Register(login, password);
    public static (bool Success, Account? Account) Login(string login, string password) => AccountRepository.Login(login, password);
    public static void SavePlayerProgress(Player player, List<Item>? storageItems = null) => AccountRepository.SavePlayerProgress(player, storageItems);
    public static void SetAdmin(string login, bool isAdmin) => AccountRepository.SetAdmin(login, isAdmin);
    public static void SetBanned(string login, bool isBanned, string reason) => AccountRepository.SetBanned(login, isBanned, reason);
    public static string? GetLoginByPlayerName(string playerName) => AccountRepository.GetLoginByPlayerName(playerName);
    public static Account? GetAccountByPlayerName(string playerName) => AccountRepository.LoadByPlayerName(playerName);

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

    // === Zones ===
    public static List<Zone> LoadZones() => ZoneRepository.LoadAll();
    public static List<WorldPortal> LoadPortals() => ZoneRepository.LoadPortals();
}

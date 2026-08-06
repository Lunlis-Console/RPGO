using Microsoft.Data.Sqlite;
using LostAndDivine.Shared.Models;

namespace LostAndDivine.Server.Repositories;

internal static class CharacterRepository
{
    public static List<CharacterInfo> ListForAccount(string login)
    {
        lock (Db.Lock)
        {
            using var conn = Db.Open();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT player_name, level, class, current_zone FROM characters WHERE account_login = $login ORDER BY created_at";
            cmd.Parameters.AddWithValue("$login", login);

            var result = new List<CharacterInfo>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                int classVal = reader.GetInt32(2);
                result.Add(new CharacterInfo
                {
                    Name = reader.GetString(0),
                    Level = reader.GetInt32(1),
                    Class = ((CharacterClass)classVal).DisplayName(),
                    Zone = reader.GetString(3)
                });
            }
            return result;
        }
    }

    public static int CountForAccount(string login)
    {
        lock (Db.Lock)
        {
            using var conn = Db.Open();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM characters WHERE account_login = $login";
            cmd.Parameters.AddWithValue("$login", login);
            return (int)(long)cmd.ExecuteScalar()!;
        }
    }

    public static bool NameTaken(string playerName)
    {
        lock (Db.Lock)
        {
            using var conn = Db.Open();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM characters WHERE player_name = $name";
            cmd.Parameters.AddWithValue("$name", playerName);
            return (long)cmd.ExecuteScalar()! > 0;
        }
    }

    public static CharacterModel Create(string login, string playerName, CharacterClass cls)
    {
        var (s, e, a, c, i, w) = cls.BaseStats();
        var character = new CharacterModel
        {
            Name = playerName,
            AccountLogin = login,
            Class = cls,
            Strength = s,
            Endurance = e,
            Agility = a,
            Cunning = c,
            Intellect = i,
            Wisdom = w,
            MaxHealth = 100 + e * 10,
            Health = 100 + e * 10,
            Mana = 100 + w * 10,
            Speed = 1 + a / 3,
            CreatedAt = DateTime.Now
        };

        lock (Db.Lock)
        {
            using var conn = Db.Open();
            using var txn = conn.BeginTransaction();
            Insert(conn, character);
            txn.Commit();
        }

        return character;
    }

    public static CharacterModel? LoadByName(string playerName)
    {
        lock (Db.Lock)
        {
            using var conn = Db.Open();
            return LoadByName(conn, playerName);
        }
    }

    private static CharacterModel? LoadByName(SqliteConnection conn, string playerName)
    {
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT player_name, account_login, class, level, experience, health, max_health, mana,
                   gold, strength, endurance, agility, cunning, intellect, wisdom,
                   attribute_points, skill_points, learned_skills, skill_ranks, speed,
                   pos_x, pos_y, hotbar_slots, current_zone, created_at
            FROM characters WHERE player_name = $name";
        cmd.Parameters.AddWithValue("$name", playerName);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        return new CharacterModel
        {
            Name = reader.GetString(0),
            AccountLogin = reader.GetString(1),
            Class = (CharacterClass)reader.GetInt32(2),
            Level = reader.GetInt32(3),
            Experience = reader.GetInt32(4),
            Health = reader.GetInt32(5),
            MaxHealth = reader.GetInt32(6),
            Mana = reader.GetInt32(7),
            Gold = reader.GetInt32(8),
            Strength = reader.GetInt32(9),
            Endurance = reader.GetInt32(10),
            Agility = reader.GetInt32(11),
            Cunning = reader.GetInt32(12),
            Intellect = reader.GetInt32(13),
            Wisdom = reader.GetInt32(14),
            AttributePoints = reader.GetInt32(15),
            SkillPoints = reader.GetInt32(16),
            LearnedSkills = ParseStringList(reader.GetString(17)),
            SkillRanks = ParseSkillRanks(reader.GetString(18)),
            Speed = reader.GetInt32(19),
            X = reader.GetInt32(20),
            Y = reader.GetInt32(21),
            HotbarSlots = LoadHotbar(reader.GetString(22)),
            CurrentZoneId = reader.GetString(23),
            CreatedAt = DateTime.Parse(reader.GetString(24)),
            Inventory = InventoryRepository.GetForPlayer(playerName),
            Equipment = InventoryRepository.LoadEquipment(conn, playerName),
            ActiveQuests = QuestRepository.Load(conn, playerName),
            CompletedQuestIds = QuestRepository.LoadCompleted(conn, playerName)
        };
    }

    public static void SavePlayerProgress(Player player, List<Item>? storageItems = null)
    {
        lock (Db.Lock)
        {
            using var conn = Db.Open();
            using var txn = conn.BeginTransaction();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE characters SET
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

            InventoryRepository.SaveEquipment(conn, player.Name, player.Equipment);

            var deleteItems = conn.CreateCommand();
            deleteItems.CommandText = "DELETE FROM inventory WHERE player_name = $name";
            deleteItems.Parameters.AddWithValue("$name", player.Name);
            deleteItems.ExecuteNonQuery();

            foreach (var item in player.Inventory)
            {
                InventoryRepository.InsertItem(conn, player.Name, item);
            }

            QuestRepository.Save(conn, player.Name, player.ActiveQuests);
            QuestRepository.SaveCompleted(conn, player.Name, player.CompletedQuestIds);

            if (storageItems != null)
                StorageRepository.SaveAll(conn, player.Name, storageItems);

            txn.Commit();
        }
    }

    public static bool DeleteCharacter(string playerName)
    {
        lock (Db.Lock)
        {
            using var conn = Db.Open();
            using var txn = conn.BeginTransaction();

            var delInv = conn.CreateCommand();
            delInv.CommandText = "DELETE FROM inventory WHERE player_name = $name";
            delInv.Parameters.AddWithValue("$name", playerName);
            delInv.ExecuteNonQuery();

            var delEq = conn.CreateCommand();
            delEq.CommandText = "DELETE FROM player_equipment WHERE player_name = $name";
            delEq.Parameters.AddWithValue("$name", playerName);
            delEq.ExecuteNonQuery();

            var delQuests = conn.CreateCommand();
            delQuests.CommandText = "DELETE FROM quests WHERE player_name = $name";
            delQuests.Parameters.AddWithValue("$name", playerName);
            delQuests.ExecuteNonQuery();

            var delCompleted = conn.CreateCommand();
            delCompleted.CommandText = "DELETE FROM player_completed_quests WHERE player_name = $name";
            delCompleted.Parameters.AddWithValue("$name", playerName);
            delCompleted.ExecuteNonQuery();

            var delStorage = conn.CreateCommand();
            delStorage.CommandText = "DELETE FROM storage_items WHERE player_name = $name";
            delStorage.Parameters.AddWithValue("$name", playerName);
            delStorage.ExecuteNonQuery();

            var delChar = conn.CreateCommand();
            delChar.CommandText = "DELETE FROM characters WHERE player_name = $name";
            delChar.Parameters.AddWithValue("$name", playerName);
            int rows = delChar.ExecuteNonQuery();

            txn.Commit();
            return rows > 0;
        }
    }

    private static void Insert(SqliteConnection conn, CharacterModel ch)
    {
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO characters (player_name, account_login, class, level, experience, health, max_health, mana, gold,
                strength, endurance, agility, cunning, intellect, wisdom, attribute_points, skill_points,
                learned_skills, skill_ranks, speed, pos_x, pos_y, hotbar_slots, current_zone, created_at, last_login)
            VALUES ($name, $login, $class, $level, $exp, $hp, $maxhp, $mana, $gold,
                $str, $end, $agi, $cun, $intel, $wis, $ap, $sp,
                $ls, $sr, $spd, $posx, $posy, $hotbar, $zone, $created, $last)";
        cmd.Parameters.AddWithValue("$name", ch.Name);
        cmd.Parameters.AddWithValue("$login", ch.AccountLogin);
        cmd.Parameters.AddWithValue("$class", (int)ch.Class);
        cmd.Parameters.AddWithValue("$level", ch.Level);
        cmd.Parameters.AddWithValue("$exp", ch.Experience);
        cmd.Parameters.AddWithValue("$hp", ch.Health);
        cmd.Parameters.AddWithValue("$maxhp", ch.MaxHealth);
        cmd.Parameters.AddWithValue("$mana", ch.Mana);
        cmd.Parameters.AddWithValue("$gold", ch.Gold);
        cmd.Parameters.AddWithValue("$str", ch.Strength);
        cmd.Parameters.AddWithValue("$end", ch.Endurance);
        cmd.Parameters.AddWithValue("$agi", ch.Agility);
        cmd.Parameters.AddWithValue("$cun", ch.Cunning);
        cmd.Parameters.AddWithValue("$intel", ch.Intellect);
        cmd.Parameters.AddWithValue("$wis", ch.Wisdom);
        cmd.Parameters.AddWithValue("$ap", ch.AttributePoints);
        cmd.Parameters.AddWithValue("$sp", ch.SkillPoints);
        cmd.Parameters.AddWithValue("$ls", System.Text.Json.JsonSerializer.Serialize(ch.LearnedSkills));
        cmd.Parameters.AddWithValue("$sr", System.Text.Json.JsonSerializer.Serialize(ch.SkillRanks));
        cmd.Parameters.AddWithValue("$spd", ch.Speed);
        cmd.Parameters.AddWithValue("$posx", ch.X);
        cmd.Parameters.AddWithValue("$posy", ch.Y);
        cmd.Parameters.AddWithValue("$hotbar", System.Text.Json.JsonSerializer.Serialize(ch.HotbarSlots));
        cmd.Parameters.AddWithValue("$zone", ch.CurrentZoneId);
        cmd.Parameters.AddWithValue("$created", ch.CreatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("$last", DateTime.Now.ToString("o"));
        cmd.ExecuteNonQuery();

        InventoryRepository.SaveEquipment(conn, ch.Name, ch.Equipment);
        foreach (var item in ch.Inventory)
            InventoryRepository.InsertItem(conn, ch.Name, item);
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

    private static List<string?> LoadHotbar(string json)
    {
        var def = new List<string?>(10) { null, null, null, null, null, null, null, null, null, null };
        if (string.IsNullOrWhiteSpace(json)) return def;
        try
        {
            var list = System.Text.Json.JsonSerializer.Deserialize<List<string?>>(json);
            if (list == null || list.Count == 0) return def;
            while (list.Count < 10) list.Add(null);
            return list.Take(10).ToList();
        }
        catch { return def; }
    }
}

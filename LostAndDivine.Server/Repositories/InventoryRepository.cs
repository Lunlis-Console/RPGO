using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;
using LostAndDivine.Shared.Models;
using LostAndDivine.Server;

namespace LostAndDivine.Server.Repositories;

internal static class InventoryRepository
{
    private static readonly ConcurrentDictionary<string, Item> _templateCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object _templateCacheLoadLock = new();

    internal static void InvalidateTemplateCache() => _templateCache.Clear();

    private static Dictionary<string, Item> GetTemplatesBatch(HashSet<string> ids)
    {
        var result = new Dictionary<string, Item>(StringComparer.OrdinalIgnoreCase);
        if (ids.Count == 0) return result;
        var missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in ids)
        {
            if (_templateCache.TryGetValue(id, out var cached))
                result[id] = cached;
            else
                missing.Add(id);
        }
        if (missing.Count == 0) return result;
        // Batch load missing templates in one query
        lock (_templateCacheLoadLock)
        {
            // double-check after lock
            var stillMissing = new HashSet<string>(missing, StringComparer.OrdinalIgnoreCase);
            foreach (var id in missing)
                if (_templateCache.ContainsKey(id))
                    stillMissing.Remove(id);
            if (stillMissing.Count == 0)
            {
                foreach (var id in ids)
                    if (!result.ContainsKey(id) && _templateCache.TryGetValue(id, out var c))
                        result[id] = c;
                return result;
            }
            using var conn = Db.OpenContent();
            var inClause = string.Join(",", stillMissing.Select((_, i) => "$p" + i));
            var cmd = conn.CreateCommand();
            cmd.CommandText = $@"SELECT id, defense, value, max_health_bonus, heal_amount, restore_mana, description,
                bonus_strength, bonus_endurance, bonus_agility, bonus_cunning, bonus_intellect, bonus_wisdom,
                bonus_phys_attack, bonus_mag_attack, bonus_resistance,
                bonus_crit_chance, bonus_crit_damage, bonus_evade_chance, bonus_attack_speed,
                bonus_block_chance, bonus_parry_chance,
                bonus_accuracy, bonus_tenacity, bonus_armor_penetration, bonus_cooldown_reduction, bonus_hp_regen, bonus_mp_regen,
                two_handed, damage_type, attack_speed_modifier, weapon_subtype,
                damage_min, damage_max, attack_range, required_level,
                max_mana_bonus,
                icon,
                bonus_defense,
                magic_defense,
                quality,
                roll_config
                FROM items WHERE id IN ({inClause})";
            int idx2 = 0;
            foreach (var id in stillMissing)
                cmd.Parameters.AddWithValue("$p" + idx2++, id);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                string tid = reader.GetString(0);
                var tmpl = new Item
                {
                    TemplateId = tid,
                    Defense = reader.GetInt32(1),
                    Value = reader.GetInt32(2),
                    MaxHealthBonus = reader.GetInt32(3),
                    HealAmount = reader.GetInt32(4),
                    RestoreMana = reader.GetInt32(5),
                    Description = reader.IsDBNull(6) ? "" : reader.GetString(6),
                    BonusStrength = reader.GetInt32(7),
                    BonusEndurance = reader.GetInt32(8),
                    BonusAgility = reader.GetInt32(9),
                    BonusCunning = reader.GetInt32(10),
                    BonusIntellect = reader.GetInt32(11),
                    BonusWisdom = reader.GetInt32(12),
                    BonusPhysAttack = reader.GetInt32(13),
                    BonusMagAttack = reader.GetInt32(14),
                    BonusResistance = reader.GetInt32(15),
                    BonusCritChance = reader.GetDouble(16),
                    BonusCritDamage = reader.GetDouble(17),
                    BonusEvadeChance = reader.GetDouble(18),
                    BonusAttackSpeed = reader.GetDouble(19),
                    BonusBlockChance = reader.GetDouble(20),
                    BonusParryChance = reader.GetDouble(21),
                    BonusAccuracy = reader.IsDBNull(22) ? 0 : reader.GetDouble(22),
                    BonusTenacity = reader.IsDBNull(23) ? 0 : reader.GetDouble(23),
                    BonusArmorPenetration = reader.IsDBNull(24) ? 0 : reader.GetDouble(24),
                    BonusCooldownReduction = reader.IsDBNull(25) ? 0 : reader.GetDouble(25),
                    BonusHpRegen = reader.IsDBNull(26) ? 0 : reader.GetDouble(26),
                    BonusMpRegen = reader.IsDBNull(27) ? 0 : reader.GetDouble(27),
                    TwoHanded = !reader.IsDBNull(28) && reader.GetInt32(28) != 0,
                    DamageType = reader.IsDBNull(29) ? "" : reader.GetString(29),
                    AttackSpeedModifier = reader.IsDBNull(30) ? 1.0 : reader.GetDouble(30),
                    WeaponSubtype = reader.IsDBNull(31) ? "" : reader.GetString(31),
                    DamageMin = reader.GetInt32(32),
                    DamageMax = reader.GetInt32(33),
                    AttackRange = reader.IsDBNull(34) ? 1 : reader.GetInt32(34),
                    RequiredLevel = reader.IsDBNull(35) ? 0 : reader.GetInt32(35),
                    MaxManaBonus = reader.IsDBNull(36) ? 0 : reader.GetInt32(36),
                    Icon = reader.IsDBNull(37) ? "" : reader.GetString(37),
                    BonusDefense = reader.IsDBNull(38) ? 0 : reader.GetInt32(38),
                    MagicDefense = reader.IsDBNull(39) ? 0 : reader.GetInt32(39),
                    Quality = reader.IsDBNull(40) ? ItemQuality.Common : (ItemQuality)reader.GetInt32(40),
                    RollConfig = ParseRollConfig(reader.IsDBNull(41) ? null : reader.GetString(41))
                };
                _templateCache[tid] = tmpl;
                result[tid] = tmpl;
            }
        }
        return result;
    }

    private static void ApplyTemplateToItem(Item item, Item tmpl)
    {
        item.Defense = tmpl.Defense;
        item.Value = tmpl.Value;
        item.HealAmount = tmpl.HealAmount;
        item.RestoreMana = tmpl.RestoreMana;
        item.Description = tmpl.Description;
        item.TwoHanded = tmpl.TwoHanded;
        item.DamageType = tmpl.DamageType;
        item.AttackSpeedModifier = tmpl.AttackSpeedModifier;
        item.WeaponSubtype = tmpl.WeaponSubtype;
        item.DamageMin = tmpl.DamageMin;
        item.DamageMax = tmpl.DamageMax;
        item.AttackRange = tmpl.AttackRange;
        item.RequiredLevel = tmpl.RequiredLevel;
        item.MaxManaBonus = tmpl.MaxManaBonus;
        item.Icon = tmpl.Icon;
        item.BonusDefense = tmpl.BonusDefense;
        item.MagicDefense = tmpl.MagicDefense;
        item.RollConfig = tmpl.RollConfig;
        if (tmpl.RollConfig is not { Enabled: true })
        {
            item.Quality = tmpl.Quality;
            item.MaxHealthBonus = tmpl.MaxHealthBonus;
            item.BonusStrength = tmpl.BonusStrength;
            item.BonusEndurance = tmpl.BonusEndurance;
            item.BonusAgility = tmpl.BonusAgility;
            item.BonusCunning = tmpl.BonusCunning;
            item.BonusIntellect = tmpl.BonusIntellect;
            item.BonusWisdom = tmpl.BonusWisdom;
            item.BonusPhysAttack = tmpl.BonusPhysAttack;
            item.BonusMagAttack = tmpl.BonusMagAttack;
            item.BonusResistance = tmpl.BonusResistance;
            item.BonusCritChance = tmpl.BonusCritChance;
            item.BonusCritDamage = tmpl.BonusCritDamage;
            item.BonusEvadeChance = tmpl.BonusEvadeChance;
            item.BonusAttackSpeed = tmpl.BonusAttackSpeed;
            item.BonusBlockChance = tmpl.BonusBlockChance;
            item.BonusParryChance = tmpl.BonusParryChance;
            item.BonusAccuracy = tmpl.BonusAccuracy;
            item.BonusTenacity = tmpl.BonusTenacity;
            item.BonusArmorPenetration = tmpl.BonusArmorPenetration;
            item.BonusCooldownReduction = tmpl.BonusCooldownReduction;
            item.BonusHpRegen = tmpl.BonusHpRegen;
            item.BonusMpRegen = tmpl.BonusMpRegen;
        }
    }

    internal static void SyncItemsFromTemplates(List<Item> items)
    {
        if (items.Count == 0) return;
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var it in items)
            if (!string.IsNullOrEmpty(it.TemplateId))
                ids.Add(it.TemplateId);
        if (ids.Count == 0) return;
        var templates = GetTemplatesBatch(ids);
        foreach (var it in items)
        {
            if (string.IsNullOrEmpty(it.TemplateId)) continue;
            if (templates.TryGetValue(it.TemplateId, out var tmpl))
                ApplyTemplateToItem(it, tmpl);
        }
    }
    internal static List<Item> GetForPlayer(string playerName)
    {
        return GetForPlayer(playerName, null);
    }

    internal static List<Item> GetForPlayer(string playerName, HashSet<string>? excludeItemIds)
    {
        List<Item> items;
        lock (Db.GameLock)
        {
            using var connection = Db.Open();
            var cmd = connection.CreateCommand();
            cmd.CommandText = @"SELECT item_id, name, type, value, defense, max_health_bonus, heal_amount, restore_mana, description,
                bonus_strength, bonus_endurance, bonus_agility, bonus_cunning, bonus_intellect, bonus_wisdom,
                bonus_phys_attack, bonus_mag_attack, bonus_resistance,
                bonus_crit_chance, bonus_crit_damage, bonus_evade_chance, bonus_attack_speed,
                bonus_block_chance, bonus_parry_chance,
                bonus_accuracy, bonus_tenacity, bonus_armor_penetration, bonus_cooldown_reduction, bonus_hp_regen, bonus_mp_regen,
                template_id, quantity,
                damage_min, damage_max, attack_range,
                max_mana_bonus,
                icon,
                magic_defense,
                quality,
                enhancement_level
                FROM inventory WHERE player_name = $name";
            cmd.Parameters.AddWithValue("$name", playerName);
            items = new List<Item>();
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
                    BonusDefense = reader.GetInt32(4),
                    MaxHealthBonus = reader.GetInt32(5),
                    HealAmount = reader.GetInt32(6),
                    RestoreMana = reader.GetInt32(7),
                    Description = reader.GetString(8),
                    BonusStrength = reader.GetInt32(9),
                    BonusEndurance = reader.GetInt32(10),
                    BonusAgility = reader.GetInt32(11),
                    BonusCunning = reader.GetInt32(12),
                    BonusIntellect = reader.GetInt32(13),
                    BonusWisdom = reader.GetInt32(14),
                    BonusPhysAttack = reader.GetInt32(15),
                    BonusMagAttack = reader.GetInt32(16),
                    BonusResistance = reader.GetInt32(17),
                    BonusCritChance = reader.GetDouble(18),
                    BonusCritDamage = reader.GetDouble(19),
                    BonusEvadeChance = reader.GetDouble(20),
                    BonusAttackSpeed = reader.GetDouble(21),
                    BonusBlockChance = reader.GetDouble(22),
                    BonusParryChance = reader.GetDouble(23),
                    BonusAccuracy = reader.IsDBNull(24) ? 0 : reader.GetDouble(24),
                    BonusTenacity = reader.IsDBNull(25) ? 0 : reader.GetDouble(25),
                    BonusArmorPenetration = reader.IsDBNull(26) ? 0 : reader.GetDouble(26),
                    BonusCooldownReduction = reader.IsDBNull(27) ? 0 : reader.GetDouble(27),
                    BonusHpRegen = reader.IsDBNull(28) ? 0 : reader.GetDouble(28),
                    BonusMpRegen = reader.IsDBNull(29) ? 0 : reader.GetDouble(29),
                    TemplateId = reader.IsDBNull(30) ? "" : reader.GetString(30),
                    Quantity = reader.IsDBNull(31) ? 1 : reader.GetInt32(31),
                    DamageMin = reader.GetInt32(32),
                    DamageMax = reader.GetInt32(33),
                    AttackRange = reader.IsDBNull(34) ? 1 : reader.GetInt32(34),
                    MaxManaBonus = reader.IsDBNull(35) ? 0 : reader.GetInt32(35),
                    Icon = reader.IsDBNull(36) ? "" : reader.GetString(36),
                    MagicDefense = reader.IsDBNull(37) ? 0 : reader.GetInt32(37),
                    Quality = reader.IsDBNull(38) ? ItemQuality.Common : (ItemQuality)reader.GetInt32(38),
                    EnhancementLevel = reader.IsDBNull(39) ? 0 : reader.GetInt32(39)
                });
            }
        }
        // Синхронизация с шаблонами — вне глобального лока, батчем (1 запрос вместо N)
        if (items.Count > 0)
            SyncItemsFromTemplates(items);
        var result = new List<Item>();
        foreach (var item in items)
        {
            item.MaxStack = Balance.MaxStackForType(item.Type);
            if (item.MaxStack <= 1 && item.Quantity > 1)
            {
                for (int k = 0; k < item.Quantity; k++)
                {
                    result.Add(new Item
                    {
                        Id = Guid.NewGuid().ToString(),
                        TemplateId = item.TemplateId,
                        Name = item.Name,
                        Type = item.Type,
                        Value = item.Value,
                        Defense = item.Defense,
                        MagicDefense = item.MagicDefense,
                        BonusDefense = item.BonusDefense,
                        MaxHealthBonus = item.MaxHealthBonus,
                        MaxManaBonus = item.MaxManaBonus,
                        Icon = item.Icon,
                        HealAmount = item.HealAmount,
                        RestoreMana = item.RestoreMana,
                        Description = item.Description,
                        MaxStack = item.MaxStack,
                        Quantity = 1,
                        BonusStrength = item.BonusStrength,
                        BonusEndurance = item.BonusEndurance,
                        BonusAgility = item.BonusAgility,
                        BonusCunning = item.BonusCunning,
                        BonusIntellect = item.BonusIntellect,
                        BonusWisdom = item.BonusWisdom,
                        BonusPhysAttack = item.BonusPhysAttack,
                        BonusMagAttack = item.BonusMagAttack,
                        BonusResistance = item.BonusResistance,
                        BonusCritChance = item.BonusCritChance,
                        BonusCritDamage = item.BonusCritDamage,
                        BonusEvadeChance = item.BonusEvadeChance,
                        BonusAttackSpeed = item.BonusAttackSpeed,
                        BonusBlockChance = item.BonusBlockChance,
                        BonusParryChance = item.BonusParryChance,
                        BonusAccuracy = item.BonusAccuracy,
                        BonusTenacity = item.BonusTenacity,
                        BonusArmorPenetration = item.BonusArmorPenetration,
                        BonusCooldownReduction = item.BonusCooldownReduction,
                        BonusHpRegen = item.BonusHpRegen,
                        BonusMpRegen = item.BonusMpRegen,
                        TwoHanded = item.TwoHanded,
                        DamageType = item.DamageType,
                        AttackSpeedModifier = item.AttackSpeedModifier,
                        DamageMin = item.DamageMin,
                        DamageMax = item.DamageMax,
                        AttackRange = item.AttackRange,
                        RequiredLevel = item.RequiredLevel,
                        Quality = item.Quality,
                        EnhancementLevel = item.EnhancementLevel
                    });
                }
            }
            else
            {
                result.Add(item);
            }
        }
        return InventoryHelper.ConsolidateStackables(result);
    }

    internal static void InsertItem(SqliteConnection connection, string playerName, Item item)
    {
        int qty = Math.Max(1, item.Quantity);

        if (Balance.MaxStackForType(item.Type) > 1)
        {
            // Стакаем по TemplateId, а если его нет — по паре (type, name)
            // (зелья, трофеи, коллекционки хранятся без template_id).
            var find = connection.CreateCommand();
            find.CommandText = @"SELECT id, quantity FROM inventory
                WHERE player_name = $name
                  AND ( (COALESCE(template_id,'') != '' AND COALESCE(template_id,'') = $tid)
                     OR (COALESCE(template_id,'') = '' AND type = $itype AND name = $iname) )
                ORDER BY quantity DESC LIMIT 1";
            find.Parameters.AddWithValue("$name", playerName);
            find.Parameters.AddWithValue("$tid", item.TemplateId);
            find.Parameters.AddWithValue("$itype", item.Type);
            find.Parameters.AddWithValue("$iname", item.Name);
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
            INSERT INTO inventory (player_name, item_id, name, type, value, defense, max_health_bonus, heal_amount, restore_mana, description,
                bonus_strength, bonus_endurance, bonus_agility, bonus_cunning, bonus_intellect, bonus_wisdom,
                bonus_phys_attack, bonus_mag_attack, bonus_resistance,
                bonus_crit_chance, bonus_crit_damage, bonus_evade_chance, bonus_attack_speed,
                bonus_block_chance, bonus_parry_chance,
                bonus_accuracy, bonus_tenacity, bonus_armor_penetration, bonus_cooldown_reduction, bonus_hp_regen, bonus_mp_regen,
                template_id, quantity,
                attack_range,
                max_mana_bonus,
                icon,
                magic_defense,
                quality,
                enhancement_level)
            VALUES ($name, $itemid, $iname, $itype, $val, $def, $mhp, $heal, $rmana, $desc,
                $str, $end, $agi, $cun, $intel, $wis,
                $pa, $ma, $res,
                $cc, $cd, $ec, $as,
                $bc, $pc, $bacc, $bten, $bap, $bcdr, $bhpr, $bmpr,
                $tid, $qty,
                $ar, $mmp, $ic, $md, $quality, $enh)";
        insertItem.Parameters.AddWithValue("$name", playerName);
        insertItem.Parameters.AddWithValue("$itemid", item.Id);
        insertItem.Parameters.AddWithValue("$iname", item.Name);
        insertItem.Parameters.AddWithValue("$itype", item.Type);
        insertItem.Parameters.AddWithValue("$val", item.Value);
        insertItem.Parameters.AddWithValue("$def", item.BonusDefense);
        insertItem.Parameters.AddWithValue("$mhp", item.MaxHealthBonus);
        insertItem.Parameters.AddWithValue("$heal", item.HealAmount);
        insertItem.Parameters.AddWithValue("$rmana", item.RestoreMana);
        insertItem.Parameters.AddWithValue("$desc", item.Description);
        insertItem.Parameters.AddWithValue("$str", item.BonusStrength);
        insertItem.Parameters.AddWithValue("$end", item.BonusEndurance);
        insertItem.Parameters.AddWithValue("$agi", item.BonusAgility);
        insertItem.Parameters.AddWithValue("$cun", item.BonusCunning);
        insertItem.Parameters.AddWithValue("$intel", item.BonusIntellect);
        insertItem.Parameters.AddWithValue("$wis", item.BonusWisdom);
        insertItem.Parameters.AddWithValue("$pa", item.BonusPhysAttack);
        insertItem.Parameters.AddWithValue("$ma", item.BonusMagAttack);
        insertItem.Parameters.AddWithValue("$res", item.BonusResistance);
        insertItem.Parameters.AddWithValue("$cc", item.BonusCritChance);
        insertItem.Parameters.AddWithValue("$cd", item.BonusCritDamage);
        insertItem.Parameters.AddWithValue("$ec", item.BonusEvadeChance);
        insertItem.Parameters.AddWithValue("$as", item.BonusAttackSpeed);
        insertItem.Parameters.AddWithValue("$bc", item.BonusBlockChance);
        insertItem.Parameters.AddWithValue("$pc", item.BonusParryChance);
        insertItem.Parameters.AddWithValue("$bacc", item.BonusAccuracy);
        insertItem.Parameters.AddWithValue("$bten", item.BonusTenacity);
        insertItem.Parameters.AddWithValue("$bap", item.BonusArmorPenetration);
        insertItem.Parameters.AddWithValue("$bcdr", item.BonusCooldownReduction);
        insertItem.Parameters.AddWithValue("$bhpr", item.BonusHpRegen);
        insertItem.Parameters.AddWithValue("$bmpr", item.BonusMpRegen);
        insertItem.Parameters.AddWithValue("$tid", item.TemplateId);
        insertItem.Parameters.AddWithValue("$qty", qty);
        insertItem.Parameters.AddWithValue("$ar", item.AttackRange);
        insertItem.Parameters.AddWithValue("$mmp", item.MaxManaBonus);
        insertItem.Parameters.AddWithValue("$ic", (object?)item.Icon ?? DBNull.Value);
        insertItem.Parameters.AddWithValue("$md", item.MagicDefense);
        insertItem.Parameters.AddWithValue("$quality", (int)item.Quality);
        insertItem.Parameters.AddWithValue("$enh", item.EnhancementLevel);
        insertItem.ExecuteNonQuery();
    }

    internal static HashSet<string> GetEquipmentIds(SqliteConnection connection, string playerName)
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

    internal static void SaveEquipment(SqliteConnection connection, string playerName, Equipment equipment)
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

    internal static Equipment LoadEquipment(SqliteConnection connection, string playerName)
    {
        var equipment = new Equipment();
        var pending = new List<(string slot, Item item)>();
        var fallbackIds = new List<(string slot, string itemId)>();
        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT slot, item_id, item_data FROM player_equipment WHERE player_name = $name";
        cmd.Parameters.AddWithValue("$name", playerName);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            string slot = reader.IsDBNull(0) ? "" : reader.GetString(0);
            if (string.IsNullOrEmpty(slot)) continue;
            if (!reader.IsDBNull(2))
            {
                var json = reader.GetString(2);
                var item = System.Text.Json.JsonSerializer.Deserialize<Item>(json);
                if (item != null)
                {
                    pending.Add((slot, item));
                    continue;
                }
            }
            string itemId = reader.IsDBNull(1) ? "" : reader.GetString(1);
            if (!string.IsNullOrEmpty(itemId))
                fallbackIds.Add((slot, itemId));
        }
        if (pending.Count > 0)
        {
            var itemsOnly = pending.Select(p => p.item).ToList();
            SyncItemsFromTemplates(itemsOnly);
            foreach (var (slot, item) in pending)
                equipment[slot] = item;
        }
        foreach (var (slot, itemId) in fallbackIds)
        {
            var item = FindItem(connection, playerName, itemId);
            if (item != null) equipment[slot] = item;
        }
        return equipment;
    }

    private static Item? FindItem(SqliteConnection connection, string playerName, string itemId)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = @"SELECT item_id, name, type, value, defense, max_health_bonus, heal_amount, restore_mana, description,
            bonus_strength, bonus_endurance, bonus_agility, bonus_cunning, bonus_intellect, bonus_wisdom,
            bonus_phys_attack, bonus_mag_attack, bonus_resistance,
            bonus_crit_chance, bonus_crit_damage, bonus_evade_chance, bonus_attack_speed,
            bonus_block_chance, bonus_parry_chance,
            bonus_accuracy, bonus_tenacity, bonus_armor_penetration, bonus_cooldown_reduction, bonus_hp_regen, bonus_mp_regen,
            template_id, quantity,
            damage_min, damage_max, attack_range,
            max_mana_bonus,
            icon,
            magic_defense,
            quality,
            enhancement_level
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
                BonusDefense = reader.GetInt32(4),
                MaxHealthBonus = reader.GetInt32(5),
                HealAmount = reader.GetInt32(6),
                RestoreMana = reader.GetInt32(7),
                Description = reader.GetString(8),
                BonusStrength = reader.GetInt32(9),
                BonusEndurance = reader.GetInt32(10),
                BonusAgility = reader.GetInt32(11),
                BonusCunning = reader.GetInt32(12),
                BonusIntellect = reader.GetInt32(13),
                BonusWisdom = reader.GetInt32(14),
                BonusPhysAttack = reader.GetInt32(15),
                BonusMagAttack = reader.GetInt32(16),
                BonusResistance = reader.GetInt32(17),
                BonusCritChance = reader.GetDouble(18),
                BonusCritDamage = reader.GetDouble(19),
                BonusEvadeChance = reader.GetDouble(20),
                BonusAttackSpeed = reader.GetDouble(21),
                BonusBlockChance = reader.GetDouble(22),
                BonusParryChance = reader.GetDouble(23),
                BonusAccuracy = reader.IsDBNull(24) ? 0 : reader.GetDouble(24),
                BonusTenacity = reader.IsDBNull(25) ? 0 : reader.GetDouble(25),
                BonusArmorPenetration = reader.IsDBNull(26) ? 0 : reader.GetDouble(26),
                BonusCooldownReduction = reader.IsDBNull(27) ? 0 : reader.GetDouble(27),
                BonusHpRegen = reader.IsDBNull(28) ? 0 : reader.GetDouble(28),
                BonusMpRegen = reader.IsDBNull(29) ? 0 : reader.GetDouble(29),
                TemplateId = reader.IsDBNull(30) ? "" : reader.GetString(30),
                Quantity = reader.IsDBNull(31) ? 1 : reader.GetInt32(31),
                DamageMin = reader.GetInt32(32),
                DamageMax = reader.GetInt32(33),
                AttackRange = reader.IsDBNull(34) ? 1 : reader.GetInt32(34),
                MaxManaBonus = reader.IsDBNull(35) ? 0 : reader.GetInt32(35),
                Icon = reader.IsDBNull(36) ? "" : reader.GetString(36),
                MagicDefense = reader.IsDBNull(37) ? 0 : reader.GetInt32(37),
                Quality = reader.IsDBNull(38) ? ItemQuality.Common : (ItemQuality)reader.GetInt32(38),
                EnhancementLevel = reader.IsDBNull(39) ? 0 : reader.GetInt32(39)
            };
            return SyncItemFromTemplate(item);
        }
        return null;
    }

    internal static Item SyncItemFromTemplate(Item item)
    {
        if (string.IsNullOrEmpty(item.TemplateId)) return item;
        var dict = GetTemplatesBatch(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { item.TemplateId });
        if (dict.TryGetValue(item.TemplateId, out var tmpl))
        {
            ApplyTemplateToItem(item, tmpl);
            Log.Debug($"[Sync] item='{item.Name}' TemplateId='{item.TemplateId}' AttackRange={item.AttackRange} WeaponSubtype='{item.WeaponSubtype}'");
        }
        return item;
    }

    private static ItemRollConfig? ParseRollConfig(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<ItemRollConfig>(json, ItemRollConfig.JsonOpts);
        }
        catch (Exception ex)
        {
            Log.Warn($"Не удалось разобрать roll_config: {ex.Message}");
            return null;
        }
    }
}

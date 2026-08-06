using Microsoft.Data.Sqlite;
using LostAndDivine.Shared.Models;
using LostAndDivine.Server;

namespace LostAndDivine.Server.Repositories;

internal static class InventoryRepository
{
    internal static List<Item> GetForPlayer(string playerName)
    {
        return GetForPlayer(playerName, null);
    }

    internal static List<Item> GetForPlayer(string playerName, HashSet<string>? excludeItemIds)
    {
        lock (Db.Lock)
        {
            using var connection = Db.Open();

            var cmd = connection.CreateCommand();
            cmd.CommandText = @"SELECT item_id, name, type, value, defense, max_health_bonus, heal_amount, restore_mana, description,
                bonus_strength, bonus_endurance, bonus_agility, bonus_cunning, bonus_intellect, bonus_wisdom,
                bonus_phys_attack, bonus_mag_attack, bonus_resistance,
                bonus_crit_chance, bonus_crit_damage, bonus_evade_chance, bonus_attack_speed,
                bonus_block_chance, bonus_parry_chance,
                template_id, quantity,
                damage_min, damage_max, attack_range
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
                    TemplateId = reader.IsDBNull(24) ? "" : reader.GetString(24),
                    Quantity = reader.IsDBNull(25) ? 1 : reader.GetInt32(25),
                    DamageMin = reader.GetInt32(26),
                    DamageMax = reader.GetInt32(27),
                    AttackRange = reader.IsDBNull(28) ? 1 : reader.GetInt32(28)
                });
            }

            var result = new List<Item>();
            foreach (var item in items)
            {
                SyncItemFromTemplate(item);
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
                            BonusDefense = item.BonusDefense,
                            MaxHealthBonus = item.MaxHealthBonus,
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
                            TwoHanded = item.TwoHanded,
                            DamageType = item.DamageType,
                            AttackSpeedModifier = item.AttackSpeedModifier,
                            DamageMin = item.DamageMin,
                            DamageMax = item.DamageMax,
                            AttackRange = item.AttackRange,
                            RequiredLevel = item.RequiredLevel
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
                template_id, quantity,
                attack_range)
            VALUES ($name, $itemid, $iname, $itype, $val, $def, $mhp, $heal, $rmana, $desc,
                $str, $end, $agi, $cun, $intel, $wis,
                $pa, $ma, $res,
                $cc, $cd, $ec, $as,
                $bc, $pc,
                $tid, $qty,
                $ar)";
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
        insertItem.Parameters.AddWithValue("$tid", item.TemplateId);
        insertItem.Parameters.AddWithValue("$qty", qty);
        insertItem.Parameters.AddWithValue("$ar", item.AttackRange);
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
                    SyncItemFromTemplate(item);
                    equipment[slot] = item;
                    continue;
                }
            }

            string itemId = reader.IsDBNull(1) ? "" : reader.GetString(1);
            if (!string.IsNullOrEmpty(itemId))
            {
                var item = FindItem(connection, playerName, itemId);
                if (item != null) equipment[slot] = item;
            }
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
            template_id, quantity,
            damage_min, damage_max, attack_range
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
                TemplateId = reader.IsDBNull(24) ? "" : reader.GetString(24),
                Quantity = reader.IsDBNull(25) ? 1 : reader.GetInt32(25),
                DamageMin = reader.GetInt32(26),
                DamageMax = reader.GetInt32(27),
                AttackRange = reader.IsDBNull(28) ? 1 : reader.GetInt32(28)
            };
            return SyncItemFromTemplate(item);
        }
        return null;
    }

    internal static Item SyncItemFromTemplate(Item item)
    {
        if (string.IsNullOrEmpty(item.TemplateId)) return item;
        using var connection = Db.OpenContent();
        var cmd = connection.CreateCommand();
        cmd.CommandText = @"SELECT defense, value, max_health_bonus, heal_amount, restore_mana, description,
            bonus_strength, bonus_endurance, bonus_agility, bonus_cunning, bonus_intellect, bonus_wisdom,
            bonus_phys_attack, bonus_mag_attack, bonus_resistance,
            bonus_crit_chance, bonus_crit_damage, bonus_evade_chance, bonus_attack_speed,
            bonus_block_chance, bonus_parry_chance,
            two_handed, damage_type, attack_speed_modifier, weapon_subtype,
            damage_min, damage_max, attack_range, required_level
            FROM items WHERE id = $tid";
        cmd.Parameters.AddWithValue("$tid", item.TemplateId);
        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            item.BonusDefense = reader.GetInt32(0);
            item.Value = reader.GetInt32(1);
            item.MaxHealthBonus = reader.GetInt32(2);
            item.HealAmount = reader.GetInt32(3);
            item.RestoreMana = reader.GetInt32(4);
            item.Description = reader.GetString(5);
            item.BonusStrength = reader.GetInt32(6);
            item.BonusEndurance = reader.GetInt32(7);
            item.BonusAgility = reader.GetInt32(8);
            item.BonusCunning = reader.GetInt32(9);
            item.BonusIntellect = reader.GetInt32(10);
            item.BonusWisdom = reader.GetInt32(11);
            item.BonusPhysAttack = reader.GetInt32(12);
            item.BonusMagAttack = reader.GetInt32(13);
            item.BonusResistance = reader.GetInt32(14);
            item.BonusCritChance = reader.GetDouble(15);
            item.BonusCritDamage = reader.GetDouble(16);
            item.BonusEvadeChance = reader.GetDouble(17);
            item.BonusAttackSpeed = reader.GetDouble(18);
            item.BonusBlockChance = reader.GetDouble(19);
            item.BonusParryChance = reader.GetDouble(20);
            item.TwoHanded = !reader.IsDBNull(21) && reader.GetInt32(21) != 0;
            item.DamageType = reader.IsDBNull(22) ? "" : reader.GetString(22);
            item.AttackSpeedModifier = reader.IsDBNull(23) ? 1.0 : reader.GetDouble(23);
            item.WeaponSubtype = reader.IsDBNull(24) ? "" : reader.GetString(24);
            item.DamageMin = reader.GetInt32(25);
            item.DamageMax = reader.GetInt32(26);
            item.AttackRange = reader.IsDBNull(27) ? 1 : reader.GetInt32(27);
            item.RequiredLevel = reader.IsDBNull(28) ? 0 : reader.GetInt32(28);
            Log.Debug($"[Sync] item='{item.Name}' TemplateId='{item.TemplateId}' AttackRange={item.AttackRange} WeaponSubtype='{item.WeaponSubtype}'");
        }
        return item;
    }
}

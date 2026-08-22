using System.Data;
using System.Globalization;
using Microsoft.Data.Sqlite;

namespace LostAndDivine.Shared.Data;

/// <summary>
/// Единый слой доступа к КОНТЕНТУ (content.db / content.editor.db), используемый
/// и редактором, и сервером. Все INSERT/UPDATE для общих таблиц (npcs, monsters,
/// monster_drops, items, quests_def, world_config, merchant_stock) собраны здесь,
/// чтобы набор колонок был описан ровно в одном месте (аудит P1-7: устранение
/// дрейфа SQL между Server и Editor).
///
/// Методы не владеют соединением — принимают открытый SqliteConnection (и
/// опционально транзакцию), как и остальной DAL проекта. Upsert-методы
/// используют ON CONFLICT(id) DO UPDATE, поэтому не затирают колонки, которые
/// текущая сторона не трогает (например, server-овые x,y при сохранении редактором).
/// </summary>
public static class ContentStore
{
    // === универсальный хелпер (бывший Db.DeleteMissingRows, P1-8) ===

    private static bool IsSafeIdentifier(string name) => System.Text.RegularExpressions.Regex.IsMatch(name, @"^[a-zA-Z_][a-zA-Z0-9_]*$");

    public static void DeleteMissingRows(SqliteConnection conn, SqliteTransaction tx, string table, string pk, IEnumerable<string> ids)
    {
        if (!IsSafeIdentifier(table)) throw new ArgumentException($"Table {table} not whitelisted", nameof(table));
        if (!IsSafeIdentifier(pk)) throw new ArgumentException($"PK {pk} not whitelisted", nameof(pk));
        var list = ids.ToList();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        if (list.Count == 0)
        {
            cmd.CommandText = $"DELETE FROM {table}";
        }
        else
        {
            var paramNames = new List<string>(list.Count);
            for (int i = 0; i < list.Count; i++)
            {
                string p = $"$p{i}";
                paramNames.Add(p);
                cmd.Parameters.AddWithValue(p, list[i]);
            }
            cmd.CommandText = $"DELETE FROM {table} WHERE {pk} NOT IN ({string.Join(",", paramNames)})";
        }
        cmd.ExecuteNonQuery();
    }

    // === npcs ===

    /// <summary>Полный upsert NPC: пишет ВСЕ колонки (id,name,type,x,y,location,data),
    /// сохраняя те, что текущая сторона не меняет (P1-7: устраняет дрейф x,y vs location).</summary>
    public static void UpsertNpc(SqliteConnection conn, SqliteTransaction? tx,
        string id, string name, string type, int x, int y, string? location, string? data, int wanderRadius = 0)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT INTO npcs (id, name, type, x, y, location, data, wander_radius) VALUES ($id,$n,$t,$x,$y,$l,$d,$wr)
            ON CONFLICT(id) DO UPDATE SET name=$n, type=$t, x=$x, y=$y, location=$l, data=$d, wander_radius=$wr";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$n", name);
        cmd.Parameters.AddWithValue("$t", type);
        cmd.Parameters.AddWithValue("$x", x);
        cmd.Parameters.AddWithValue("$y", y);
        cmd.Parameters.AddWithValue("$l", (object?)location ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$d", (object?)data ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$wr", wanderRadius);
        cmd.ExecuteNonQuery();
    }

    public static void UpdateNpcLocation(SqliteConnection conn, SqliteTransaction? tx, string id, string location)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE npcs SET location = $z WHERE id = $i";
        cmd.Parameters.AddWithValue("$z", location);
        cmd.Parameters.AddWithValue("$i", id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Целевой UPDATE поля data (JSON диалога). Возвращает число затронутых строк.</summary>
    public static int UpdateNpcDialogue(SqliteConnection conn, SqliteTransaction? tx, string id, string json)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE npcs SET data = $d WHERE id = $id";
        cmd.Parameters.AddWithValue("$d", json);
        cmd.Parameters.AddWithValue("$id", id);
        return cmd.ExecuteNonQuery();
    }

    // === world_config ===

    public static void UpdateWorldConfig(SqliteConnection conn, SqliteTransaction? tx, string key, string value)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE world_config SET value = $v WHERE key = $k";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
    }

    public static string? GetWorldConfig(SqliteConnection conn, string key)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM world_config WHERE key = $k";
        cmd.Parameters.AddWithValue("$k", key);
        var v = cmd.ExecuteScalar();
        return v == null || v == DBNull.Value ? null : v.ToString();
    }

    // === monsters ===

    public static void UpsertMonster(SqliteConnection conn, SqliteTransaction? tx, DataRow row)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT INTO monsters (id, name, tier, health, phys_attack, phys_defense, xp_reward, gold_reward, gold_max,
                symbol, strength, endurance, agility, cunning, intellect, wisdom, crit_chance, crit_damage,
                evade_chance, block_chance, parry_chance, shield_defense)
            VALUES ($id,$n,$t,$hp,$a,$d,$xp,$g,$gm,$s,$str,$sta,$agi,$cun,$wis,$wil,$cc,$cd,$ec,$bc,$pc,$sd)
            ON CONFLICT(id) DO UPDATE SET name=$n, tier=$t, health=$hp, phys_attack=$a, phys_defense=$d,
                xp_reward=$xp, gold_reward=$g, gold_max=$gm, symbol=$s, strength=$str, endurance=$sta,
                agility=$agi, cunning=$cun, intellect=$wis, wisdom=$wil, crit_chance=$cc, crit_damage=$cd,
                evade_chance=$ec, block_chance=$bc, parry_chance=$pc, shield_defense=$sd";
        cmd.Parameters.AddWithValue("$id", RowStr(row, "id"));
        cmd.Parameters.AddWithValue("$n", RowStr(row, "name"));
        cmd.Parameters.AddWithValue("$t", ToInt(row["tier"]));
        cmd.Parameters.AddWithValue("$hp", ToInt(row["health"]));
        cmd.Parameters.AddWithValue("$a", ToInt(row["phys_attack"]));
        cmd.Parameters.AddWithValue("$d", ToInt(row["phys_defense"]));
        cmd.Parameters.AddWithValue("$xp", ToInt(row["xp_reward"]));
        cmd.Parameters.AddWithValue("$g", ToInt(row["gold_reward"]));
        cmd.Parameters.AddWithValue("$gm", ToInt(row["gold_max"]));
        cmd.Parameters.AddWithValue("$s", FirstChar(RowStr(row, "symbol"), "M"));
        cmd.Parameters.AddWithValue("$str", ToInt(row["strength"]));
        cmd.Parameters.AddWithValue("$sta", ToInt(row["endurance"]));
        cmd.Parameters.AddWithValue("$agi", ToInt(row["agility"]));
        cmd.Parameters.AddWithValue("$cun", ToInt(row["cunning"]));
        cmd.Parameters.AddWithValue("$wis", ToInt(row["intellect"]));
        cmd.Parameters.AddWithValue("$wil", ToInt(row["wisdom"]));
        cmd.Parameters.AddWithValue("$cc", ToDouble(row["crit_chance"]));
        cmd.Parameters.AddWithValue("$cd", ToDouble(row["crit_damage"]));
        cmd.Parameters.AddWithValue("$ec", ToDouble(row["evade_chance"]));
        cmd.Parameters.AddWithValue("$bc", ToDouble(row["block_chance"]));
        cmd.Parameters.AddWithValue("$pc", ToDouble(row["parry_chance"]));
        cmd.Parameters.AddWithValue("$sd", ToInt(row["shield_defense"]));
        cmd.ExecuteNonQuery();
    }

    public static void DeleteAllMonsterDrops(SqliteConnection conn, SqliteTransaction? tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "DELETE FROM monster_drops";
        cmd.ExecuteNonQuery();
    }

    public static void InsertMonsterDrop(SqliteConnection conn, SqliteTransaction? tx, string monsterId, string itemId, int chance)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO monster_drops (monster_id, item_id, drop_chance) VALUES ($mid, $iid, $dc)";
        cmd.Parameters.AddWithValue("$mid", monsterId);
        cmd.Parameters.AddWithValue("$iid", itemId);
        cmd.Parameters.AddWithValue("$dc", Math.Clamp(chance, 0, 100));
        cmd.ExecuteNonQuery();
    }

    // === items ===

    public static void UpsertItem(SqliteConnection conn, SqliteTransaction? tx, DataRow row)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT INTO items (id, name, type, value, damage_min, damage_max, defense, max_health_bonus, max_mana_bonus,
                heal_amount, restore_mana, stock, description, bonus_strength, bonus_endurance, bonus_agility,
                bonus_cunning, bonus_intellect, bonus_wisdom, bonus_phys_attack, bonus_mag_attack, bonus_defense,
                bonus_resistance, bonus_attack_speed, bonus_crit_chance, bonus_crit_damage, bonus_evade_chance,
                bonus_block_chance, bonus_parry_chance, bonus_accuracy, bonus_tenacity, bonus_armor_penetration,
                bonus_cooldown_reduction, bonus_hp_regen, bonus_mp_regen, two_handed, damage_type,
                attack_speed_modifier, weapon_subtype, attack_range, required_level, quest_item, icon, magic_defense, quality, roll_config)
            VALUES ($id,$n,$t,$v,$dmn,$dmx,$d,$m,$mm,$h,$rm,$s,$desc,$str,$sta,$agi,$cun,$wis,$wil,$bpa,$bma,$bdef,$bres,
                $bas,$cc,$cd,$ec,$blk,$prr,$acc,$ten,$arp,$cdr,$hpr,$mpr,$th,$dt,$asm,$ws,$ar,$rl,$qi,$ic,$md,$q,$rc)
            ON CONFLICT(id) DO UPDATE SET name=$n, type=$t, value=$v, damage_min=$dmn, damage_max=$dmx, defense=$d,
                max_health_bonus=$m, max_mana_bonus=$mm, heal_amount=$h, restore_mana=$rm, stock=$s, description=$desc,
                bonus_strength=$str, bonus_endurance=$sta, bonus_agility=$agi, bonus_cunning=$cun, bonus_intellect=$wis,
                bonus_wisdom=$wil, bonus_phys_attack=$bpa, bonus_mag_attack=$bma, bonus_defense=$bdef, bonus_resistance=$bres,
                bonus_attack_speed=$bas, bonus_crit_chance=$cc, bonus_crit_damage=$cd, bonus_evade_chance=$ec,
                bonus_block_chance=$blk, bonus_parry_chance=$prr, bonus_accuracy=$acc, bonus_tenacity=$ten,
                bonus_armor_penetration=$arp, bonus_cooldown_reduction=$cdr, bonus_hp_regen=$hpr, bonus_mp_regen=$mpr,
                two_handed=$th, damage_type=$dt, attack_speed_modifier=$asm, weapon_subtype=$ws, attack_range=$ar,
                required_level=$rl, quest_item=$qi, icon=$ic, magic_defense=$md, quality=$q, roll_config=$rc";
        cmd.Parameters.AddWithValue("$id", RowStr(row, "id"));
        cmd.Parameters.AddWithValue("$n", RowStr(row, "name"));
        cmd.Parameters.AddWithValue("$t", RowStr(row, "type"));
        cmd.Parameters.AddWithValue("$v", ToInt(row["value"]));
        cmd.Parameters.AddWithValue("$dmn", ToInt(row["damage_min"]));
        cmd.Parameters.AddWithValue("$dmx", ToInt(row["damage_max"]));
        cmd.Parameters.AddWithValue("$d", ToInt(row["defense"]));
        cmd.Parameters.AddWithValue("$m", ToInt(row["max_health_bonus"]));
        cmd.Parameters.AddWithValue("$mm", ToInt(row["max_mana_bonus"]));
        cmd.Parameters.AddWithValue("$h", ToInt(row["heal_amount"]));
        cmd.Parameters.AddWithValue("$rm", ToInt(row["restore_mana"]));
        cmd.Parameters.AddWithValue("$s", ToInt(row["stock"]));
        cmd.Parameters.AddWithValue("$desc", RowStr(row, "description"));
        cmd.Parameters.AddWithValue("$str", ToInt(row["bonus_strength"]));
        cmd.Parameters.AddWithValue("$sta", ToInt(row["bonus_endurance"]));
        cmd.Parameters.AddWithValue("$agi", ToInt(row["bonus_agility"]));
        cmd.Parameters.AddWithValue("$cun", ToInt(row["bonus_cunning"]));
        cmd.Parameters.AddWithValue("$wis", ToInt(row["bonus_intellect"]));
        cmd.Parameters.AddWithValue("$wil", ToInt(row["bonus_wisdom"]));
        cmd.Parameters.AddWithValue("$bpa", ToInt(row["bonus_phys_attack"]));
        cmd.Parameters.AddWithValue("$bma", ToInt(row["bonus_mag_attack"]));
        cmd.Parameters.AddWithValue("$bdef", ToInt(row["bonus_defense"]));
        cmd.Parameters.AddWithValue("$bres", ToInt(row["bonus_resistance"]));
        cmd.Parameters.AddWithValue("$bas", ToDouble(row["bonus_attack_speed"]));
        cmd.Parameters.AddWithValue("$cc", ToDouble(row["bonus_crit_chance"]));
        cmd.Parameters.AddWithValue("$cd", ToDouble(row["bonus_crit_damage"]));
        cmd.Parameters.AddWithValue("$ec", ToDouble(row["bonus_evade_chance"]));
        cmd.Parameters.AddWithValue("$blk", ToDouble(row["bonus_block_chance"]));
        cmd.Parameters.AddWithValue("$prr", ToDouble(row["bonus_parry_chance"]));
        cmd.Parameters.AddWithValue("$acc", ToDouble(row["bonus_accuracy"]));
        cmd.Parameters.AddWithValue("$ten", ToDouble(row["bonus_tenacity"]));
        cmd.Parameters.AddWithValue("$arp", ToDouble(row["bonus_armor_penetration"]));
        cmd.Parameters.AddWithValue("$cdr", ToDouble(row["bonus_cooldown_reduction"]));
        cmd.Parameters.AddWithValue("$hpr", ToDouble(row["bonus_hp_regen"]));
        cmd.Parameters.AddWithValue("$mpr", ToDouble(row["bonus_mp_regen"]));
        cmd.Parameters.AddWithValue("$th", ToInt(row["two_handed"]));
        cmd.Parameters.AddWithValue("$dt", RowStr(row, "damage_type"));
        cmd.Parameters.AddWithValue("$asm", ToDouble(row["attack_speed_modifier"]));
        cmd.Parameters.AddWithValue("$ws", RowStr(row, "weapon_subtype"));
        cmd.Parameters.AddWithValue("$ar", ToInt(row["attack_range"]));
        cmd.Parameters.AddWithValue("$rl", ToInt(row["required_level"]));
        cmd.Parameters.AddWithValue("$qi", QuestFlag(row["quest_item"]));
        cmd.Parameters.AddWithValue("$ic", RowStr(row, "icon"));
        cmd.Parameters.AddWithValue("$md", ToInt(row["magic_defense"]));
        cmd.Parameters.AddWithValue("$q", ToInt(row["quality"]));
        cmd.Parameters.AddWithValue("$rc", RowStr(row, "roll_config"));
        cmd.ExecuteNonQuery();
    }

    // === quests_def ===

    public static void UpsertQuest(SqliteConnection conn, SqliteTransaction? tx,
        string id, string title, string description, string type,
        string targetMonsterId, string targetItemId, string targetNpcId,
        int target, int xpReward, int goldReward, string chainId, int step,
        string prereq, int minLevel, string itemRewardId, int itemRewardCount,
        string targetZoneId, int targetX, int targetY, int autoGrant,
        string giverNpcId, int isStory, string location, int repeatable, string objectives)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT INTO quests_def (id, title, description, type, target_monster_id, target_item_id, target_npc_id, target,
                xp_reward, gold_reward, chain_id, step, prerequisite_quest_id, min_level, item_reward_id, item_reward_count,
                target_zone_id, target_x, target_y, auto_grant, giver_npc_id, is_story, location, repeatable, objectives)
            VALUES ($id,$t,$d,$ty,$tm,$ti,$tn,$tg,$xp,$g,$ch,$st,$pr,$ml,$ri,$rc,$tz,$tx,$tyy,$ag,$gn,$is,$loc,$rep,$obj)
            ON CONFLICT(id) DO UPDATE SET title=$t, description=$d, type=$ty, target_monster_id=$tm, target_item_id=$ti,
                target_npc_id=$tn, target=$tg, xp_reward=$xp, gold_reward=$g, chain_id=$ch, step=$st,
                prerequisite_quest_id=$pr, min_level=$ml, item_reward_id=$ri, item_reward_count=$rc, target_zone_id=$tz,
                target_x=$tx, target_y=$tyy, auto_grant=$ag, giver_npc_id=$gn, is_story=$is, location=$loc,
                repeatable=$rep, objectives=$obj";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$t", title);
        cmd.Parameters.AddWithValue("$d", description);
        cmd.Parameters.AddWithValue("$ty", type);
        cmd.Parameters.AddWithValue("$tm", targetMonsterId);
        cmd.Parameters.AddWithValue("$ti", targetItemId);
        cmd.Parameters.AddWithValue("$tn", targetNpcId);
        cmd.Parameters.AddWithValue("$tg", target);
        cmd.Parameters.AddWithValue("$xp", xpReward);
        cmd.Parameters.AddWithValue("$g", goldReward);
        cmd.Parameters.AddWithValue("$ch", chainId);
        cmd.Parameters.AddWithValue("$st", step);
        cmd.Parameters.AddWithValue("$pr", prereq);
        cmd.Parameters.AddWithValue("$ml", minLevel);
        cmd.Parameters.AddWithValue("$ri", itemRewardId);
        cmd.Parameters.AddWithValue("$rc", itemRewardCount);
        cmd.Parameters.AddWithValue("$tz", targetZoneId);
        cmd.Parameters.AddWithValue("$tx", targetX);
        cmd.Parameters.AddWithValue("$tyy", targetY);
        cmd.Parameters.AddWithValue("$ag", autoGrant);
        cmd.Parameters.AddWithValue("$gn", giverNpcId);
        cmd.Parameters.AddWithValue("$is", isStory);
        cmd.Parameters.AddWithValue("$loc", location);
        cmd.Parameters.AddWithValue("$rep", repeatable);
        cmd.Parameters.AddWithValue("$obj", objectives);
        cmd.ExecuteNonQuery();
    }

    // === merchant_stock ===

    public static void DeleteMerchantStock(SqliteConnection conn, SqliteTransaction? tx, string npcId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "DELETE FROM merchant_stock WHERE npc_id = $npc";
        cmd.Parameters.AddWithValue("$npc", npcId);
        cmd.ExecuteNonQuery();
    }

    public static void InsertMerchantStock(SqliteConnection conn, SqliteTransaction? tx, string npcId, string itemId, int stock)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO merchant_stock (npc_id, item_id, stock) VALUES ($npc, $item, $stock)";
        cmd.Parameters.AddWithValue("$npc", npcId);
        cmd.Parameters.AddWithValue("$item", itemId);
        cmd.Parameters.AddWithValue("$stock", Math.Max(1, stock));
        cmd.ExecuteNonQuery();
    }

    // === хелперы ===

    private static string RowStr(DataRow row, string col)
        => row[col]?.ToString() ?? "";

    private static int ToInt(object? v) => int.TryParse(v?.ToString(), out int r) ? r : 0;

    private static double ToDouble(object? v)
        => double.TryParse(v?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double r) ? r : 0;

    private static int QuestFlag(object? v) => v is bool b ? (b ? 1 : 0) : ToInt(v);

    private static string FirstChar(string s, string fallback)
        => string.IsNullOrEmpty(s) ? fallback : (s.Length > 0 ? s[0].ToString() : fallback);
}

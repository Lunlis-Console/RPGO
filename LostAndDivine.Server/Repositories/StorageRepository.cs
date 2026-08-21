using Microsoft.Data.Sqlite;
using LostAndDivine.Shared.Models;

namespace LostAndDivine.Server.Repositories;

internal static class StorageRepository
{
    /// <summary>
    /// Перезаписывает содержимое склада игрока в рамках переданного соединения/транзакции.
    /// Используется вместе с сохранением инвентаря, чтобы перенос предметов
    /// между инвентарём и складом сохранялся атомарно (без потери/дублирования).
    /// </summary>
    internal static void SaveAll(SqliteConnection connection, string playerName, List<Item> items)
    {
        var delete = connection.CreateCommand();
        delete.CommandText = "DELETE FROM storage_items WHERE player_name = $name";
        delete.Parameters.AddWithValue("$name", playerName);
        delete.ExecuteNonQuery();

        foreach (var item in items)
        {
            var insert = connection.CreateCommand();
            insert.CommandText = @"INSERT INTO storage_items
                (player_name, item_id, template_id, name, type, value, quantity, max_stack,
                 description, bonus_defense, bonus_phys_attack, bonus_mag_attack,
                 bonus_max_health, bonus_max_mana, heal_amount, restore_mana, weapon_subtype,
                 damage_min, damage_max, attack_range, required_level, icon,
                 magic_defense)
                VALUES ($name, $id, $tid, $iname, $itype, $val, $qty, $ms,
                        $desc, $bd, $bpa, $bma, $bmh, $bmm, $ha, $rm, $ws, $dmn, $dmx, $ar, $rl, $ic, $md)";
            insert.Parameters.AddWithValue("$name", playerName);
            insert.Parameters.AddWithValue("$id", item.Id);
            insert.Parameters.AddWithValue("$tid", item.TemplateId);
            insert.Parameters.AddWithValue("$iname", item.Name);
            insert.Parameters.AddWithValue("$itype", item.Type);
            insert.Parameters.AddWithValue("$val", item.Value);
            insert.Parameters.AddWithValue("$qty", item.Quantity);
            insert.Parameters.AddWithValue("$ms", item.MaxStack);
            insert.Parameters.AddWithValue("$desc", item.Description);
            insert.Parameters.AddWithValue("$bd", item.BonusDefense);
            insert.Parameters.AddWithValue("$bpa", item.BonusPhysAttack);
            insert.Parameters.AddWithValue("$bma", item.BonusMagAttack);
            insert.Parameters.AddWithValue("$bmh", item.MaxHealthBonus);
            insert.Parameters.AddWithValue("$bmm", item.MaxManaBonus);
            insert.Parameters.AddWithValue("$ic", (object?)item.Icon ?? DBNull.Value);
            insert.Parameters.AddWithValue("$ha", item.HealAmount);
            insert.Parameters.AddWithValue("$rm", item.RestoreMana);
            insert.Parameters.AddWithValue("$ws", item.WeaponSubtype);
            insert.Parameters.AddWithValue("$dmn", item.DamageMin);
            insert.Parameters.AddWithValue("$dmx", item.DamageMax);
            insert.Parameters.AddWithValue("$ar", item.AttackRange);
            insert.Parameters.AddWithValue("$rl", item.RequiredLevel);
            insert.Parameters.AddWithValue("$md", item.MagicDefense);
            insert.ExecuteNonQuery();
        }
    }
}



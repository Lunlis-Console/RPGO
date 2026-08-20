using System.Text.Json;
using LostAndDivine.Shared.Models;

namespace LostAndDivine.Server.Repositories;

internal static class ItemRepository
{
    internal static List<Item> LoadAll()
    {
        lock (Db.Lock)
        {
            var result = new List<Item>();
            using var connection = Db.OpenContent();
            var cmd = connection.CreateCommand();
            cmd.CommandText = @"SELECT id, name, type, value, defense, max_health_bonus, heal_amount, restore_mana, stock, description,
                bonus_strength, bonus_endurance, bonus_agility, bonus_cunning, bonus_intellect, bonus_wisdom,
                bonus_phys_attack, bonus_mag_attack, bonus_resistance,
                bonus_crit_chance, bonus_crit_damage, bonus_evade_chance, bonus_attack_speed,
                bonus_block_chance, bonus_parry_chance,
                bonus_accuracy, bonus_tenacity, bonus_armor_penetration, bonus_cooldown_reduction, bonus_hp_regen, bonus_mp_regen,
                two_handed, damage_type, attack_speed_modifier, weapon_subtype,
                damage_min, damage_max, attack_range, required_level,
                quest_item,
                max_mana_bonus,
                icon,
                magic_defense,
                bonus_defense,
                quality,
                roll_config
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
                    Defense = reader.GetInt32(4),
                    MaxHealthBonus = reader.GetInt32(5),
                    HealAmount = reader.GetInt32(6),
                    RestoreMana = reader.GetInt32(7),
                    Stock = reader.GetInt32(8),
                    Description = reader.GetString(9),
                    BonusStrength = reader.GetInt32(10),
                    BonusEndurance = reader.GetInt32(11),
                    BonusAgility = reader.GetInt32(12),
                    BonusCunning = reader.GetInt32(13),
                    BonusIntellect = reader.GetInt32(14),
                    BonusWisdom = reader.GetInt32(15),
                    BonusPhysAttack = reader.GetInt32(16),
                    BonusMagAttack = reader.GetInt32(17),
                    BonusResistance = reader.GetInt32(18),
                    BonusCritChance = reader.GetDouble(19),
                    BonusCritDamage = reader.GetDouble(20),
                    BonusEvadeChance = reader.GetDouble(21),
                    BonusAttackSpeed = reader.GetDouble(22),
                    BonusBlockChance = reader.GetDouble(23),
                    BonusParryChance = reader.GetDouble(24),
                    BonusAccuracy = reader.IsDBNull(25) ? 0 : reader.GetDouble(25),
                    BonusTenacity = reader.IsDBNull(26) ? 0 : reader.GetDouble(26),
                    BonusArmorPenetration = reader.IsDBNull(27) ? 0 : reader.GetDouble(27),
                    BonusCooldownReduction = reader.IsDBNull(28) ? 0 : reader.GetDouble(28),
                    BonusHpRegen = reader.IsDBNull(29) ? 0 : reader.GetDouble(29),
                    BonusMpRegen = reader.IsDBNull(30) ? 0 : reader.GetDouble(30),
                    TwoHanded = reader.GetInt32(31) != 0,
                    DamageType = reader.IsDBNull(32) ? "" : reader.GetString(32),
                    AttackSpeedModifier = reader.IsDBNull(33) ? 1.0 : reader.GetDouble(33),
                    WeaponSubtype = reader.IsDBNull(34) ? "" : reader.GetString(34),
                    DamageMin = reader.GetInt32(35),
                    DamageMax = reader.GetInt32(36),
                    AttackRange = reader.IsDBNull(37) ? 1 : reader.GetInt32(37),
                    RequiredLevel = reader.IsDBNull(38) ? 0 : reader.GetInt32(38),
                    MaxStack = Balance.MaxStackForType(reader.GetString(2)),
                    QuestItem = !reader.IsDBNull(39) && reader.GetInt32(39) != 0,
                    MaxManaBonus = reader.IsDBNull(40) ? 0 : reader.GetInt32(40),
                    Icon = reader.IsDBNull(41) ? "" : reader.GetString(41),
                    MagicDefense = reader.IsDBNull(42) ? 0 : reader.GetInt32(42),
                    BonusDefense = reader.IsDBNull(43) ? 0 : reader.GetInt32(43),
                    Quality = reader.IsDBNull(44) ? ItemQuality.Common : (ItemQuality)reader.GetInt32(44),
                    RollConfig = ParseRollConfig(reader.IsDBNull(45) ? null : reader.GetString(45)),
                });
            }
            return result;
        }
    }

    internal static Item? GetTemplate(string templateId)
    {
        lock (Db.Lock)
        {
            using var connection = Db.OpenContent();
            var cmd = connection.CreateCommand();
            cmd.CommandText = @"SELECT id, name, type, value, defense, max_health_bonus, heal_amount, restore_mana, stock, description,
                bonus_strength, bonus_endurance, bonus_agility, bonus_cunning, bonus_intellect, bonus_wisdom,
                bonus_phys_attack, bonus_mag_attack, bonus_resistance,
                bonus_crit_chance, bonus_crit_damage, bonus_evade_chance, bonus_attack_speed,
                bonus_block_chance, bonus_parry_chance,
                bonus_accuracy, bonus_tenacity, bonus_armor_penetration, bonus_cooldown_reduction, bonus_hp_regen, bonus_mp_regen,
                two_handed, damage_type, attack_speed_modifier, weapon_subtype,
                damage_min, damage_max, attack_range, required_level,
                quest_item,
                max_mana_bonus,
                icon,
                magic_defense,
                bonus_defense,
                quality,
                roll_config
                FROM items WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", templateId);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return null;
            return new Item
            {
                Id = Guid.NewGuid().ToString(),
                TemplateId = reader.GetString(0),
                Name = reader.GetString(1),
                Type = reader.GetString(2),
                Value = reader.GetInt32(3),
                Defense = reader.GetInt32(4),
                MaxHealthBonus = reader.GetInt32(5),
                HealAmount = reader.GetInt32(6),
                RestoreMana = reader.GetInt32(7),
                Stock = reader.GetInt32(8),
                Description = reader.GetString(9),
                BonusStrength = reader.GetInt32(10),
                BonusEndurance = reader.GetInt32(11),
                BonusAgility = reader.GetInt32(12),
                BonusCunning = reader.GetInt32(13),
                BonusIntellect = reader.GetInt32(14),
                BonusWisdom = reader.GetInt32(15),
                BonusPhysAttack = reader.GetInt32(16),
                BonusMagAttack = reader.GetInt32(17),
                BonusResistance = reader.GetInt32(18),
                BonusCritChance = reader.GetDouble(19),
                BonusCritDamage = reader.GetDouble(20),
                BonusEvadeChance = reader.GetDouble(21),
                BonusAttackSpeed = reader.GetDouble(22),
                BonusBlockChance = reader.GetDouble(23),
                BonusParryChance = reader.GetDouble(24),
                BonusAccuracy = reader.IsDBNull(25) ? 0 : reader.GetDouble(25),
                BonusTenacity = reader.IsDBNull(26) ? 0 : reader.GetDouble(26),
                BonusArmorPenetration = reader.IsDBNull(27) ? 0 : reader.GetDouble(27),
                BonusCooldownReduction = reader.IsDBNull(28) ? 0 : reader.GetDouble(28),
                BonusHpRegen = reader.IsDBNull(29) ? 0 : reader.GetDouble(29),
                BonusMpRegen = reader.IsDBNull(30) ? 0 : reader.GetDouble(30),
                TwoHanded = reader.GetInt32(31) != 0,
                DamageType = reader.IsDBNull(32) ? "" : reader.GetString(32),
                AttackSpeedModifier = reader.IsDBNull(33) ? 1.0 : reader.GetDouble(33),
                WeaponSubtype = reader.IsDBNull(34) ? "" : reader.GetString(34),
                DamageMin = reader.GetInt32(35),
                DamageMax = reader.GetInt32(36),
                AttackRange = reader.IsDBNull(37) ? 1 : reader.GetInt32(37),
                RequiredLevel = reader.IsDBNull(38) ? 0 : reader.GetInt32(38),
                MaxStack = Balance.MaxStackForType(reader.GetString(2)),
                QuestItem = !reader.IsDBNull(39) && reader.GetInt32(39) != 0,
                MaxManaBonus = reader.IsDBNull(40) ? 0 : reader.GetInt32(40),
                Icon = reader.IsDBNull(41) ? "" : reader.GetString(41),
                MagicDefense = reader.IsDBNull(42) ? 0 : reader.GetInt32(42),
                BonusDefense = reader.IsDBNull(43) ? 0 : reader.GetInt32(43),
                Quality = reader.IsDBNull(44) ? ItemQuality.Common : (ItemQuality)reader.GetInt32(44),
                RollConfig = ParseRollConfig(reader.IsDBNull(45) ? null : reader.GetString(45)),
            };
        }
    }

    private static ItemRollConfig? ParseRollConfig(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<ItemRollConfig>(json, ItemRollConfig.JsonOpts);
        }
        catch (Exception ex)
        {
            Log.Warn($"Не удалось разобрать roll_config: {ex.Message}");
            return null;
        }
    }
}

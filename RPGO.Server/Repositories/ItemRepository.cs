using RPGGame.Shared.Models;

namespace RPGGame.Server.Repositories;

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
                two_handed, damage_type, attack_speed_modifier, weapon_subtype,
                damage_min, damage_max, attack_range, required_level
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
                    BonusDefense = reader.GetInt32(4),
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
                    TwoHanded = reader.GetInt32(25) != 0,
                    DamageType = reader.IsDBNull(26) ? "" : reader.GetString(26),
                    AttackSpeedModifier = reader.IsDBNull(27) ? 1.0 : reader.GetDouble(27),
                    WeaponSubtype = reader.IsDBNull(28) ? "" : reader.GetString(28),
                    DamageMin = reader.GetInt32(29),
                    DamageMax = reader.GetInt32(30),
                    AttackRange = reader.IsDBNull(31) ? 1 : reader.GetInt32(31),
                    RequiredLevel = reader.IsDBNull(32) ? 0 : reader.GetInt32(32),
                    MaxStack = Balance.MaxStackForType(reader.GetString(2)),
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
                two_handed, damage_type, attack_speed_modifier, weapon_subtype,
                damage_min, damage_max, attack_range, required_level
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
                BonusDefense = reader.GetInt32(4),
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
                TwoHanded = reader.GetInt32(25) != 0,
                DamageType = reader.IsDBNull(26) ? "" : reader.GetString(26),
                AttackSpeedModifier = reader.IsDBNull(27) ? 1.0 : reader.GetDouble(27),
                WeaponSubtype = reader.IsDBNull(28) ? "" : reader.GetString(28),
                DamageMin = reader.GetInt32(29),
                DamageMax = reader.GetInt32(30),
                AttackRange = reader.IsDBNull(31) ? 1 : reader.GetInt32(31),
                RequiredLevel = reader.IsDBNull(32) ? 0 : reader.GetInt32(32),
                MaxStack = Balance.MaxStackForType(reader.GetString(2)),
            };
        }
    }
}

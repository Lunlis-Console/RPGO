using RPGGame.Shared.Models;

namespace RPGGame.Server.Repositories;

internal static class ItemRepository
{
    internal static List<Item> LoadAll()
    {
        lock (Db.Lock)
        {
            var result = new List<Item>();
            using var connection = Db.Open();
            var cmd = connection.CreateCommand();
            cmd.CommandText = @"SELECT id, name, type, value, defense, max_health_bonus, heal_amount, stock, description,
                bonus_strength, bonus_endurance, bonus_agility, bonus_cunning, bonus_intellect, bonus_wisdom,
                bonus_phys_attack, bonus_mag_attack, bonus_resistance,
                bonus_crit_chance, bonus_crit_damage, bonus_evade_chance, bonus_attack_speed,
                two_handed, damage_type, attack_speed_modifier, weapon_subtype,
                damage_min, damage_max, attack_range
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
                    Stock = reader.GetInt32(7),
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
                    TwoHanded = reader.GetInt32(22) != 0,
                    DamageType = reader.IsDBNull(23) ? "" : reader.GetString(23),
                    AttackSpeedModifier = reader.IsDBNull(24) ? 1.0 : reader.GetDouble(24),
                    WeaponSubtype = reader.IsDBNull(25) ? "" : reader.GetString(25),
                    DamageMin = reader.GetInt32(26),
                    DamageMax = reader.GetInt32(27),
                    AttackRange = reader.IsDBNull(28) ? 1 : reader.GetInt32(28),
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
            using var connection = Db.Open();
            var cmd = connection.CreateCommand();
            cmd.CommandText = @"SELECT id, name, type, value, defense, max_health_bonus, heal_amount, stock, description,
                bonus_strength, bonus_endurance, bonus_agility, bonus_cunning, bonus_intellect, bonus_wisdom,
                bonus_phys_attack, bonus_mag_attack, bonus_resistance,
                bonus_crit_chance, bonus_crit_damage, bonus_evade_chance, bonus_attack_speed,
                two_handed, damage_type, attack_speed_modifier, weapon_subtype,
                damage_min, damage_max, attack_range
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
                Stock = reader.GetInt32(7),
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
                TwoHanded = reader.GetInt32(22) != 0,
                DamageType = reader.IsDBNull(23) ? "" : reader.GetString(23),
                AttackSpeedModifier = reader.IsDBNull(24) ? 1.0 : reader.GetDouble(24),
                WeaponSubtype = reader.IsDBNull(25) ? "" : reader.GetString(25),
                DamageMin = reader.GetInt32(26),
                DamageMax = reader.GetInt32(27),
                AttackRange = reader.IsDBNull(28) ? 1 : reader.GetInt32(28),
                MaxStack = Balance.MaxStackForType(reader.GetString(2)),
            };
        }
    }
}

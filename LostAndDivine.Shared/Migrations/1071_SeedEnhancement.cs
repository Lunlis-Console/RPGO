using FluentMigrator;

namespace LostAndDivine.Shared.Migrations;

/// <summary>
/// Сид: два камня усиления (оружие/броня) и NPC «Кузнец» (blacksmith) в городе.
/// Камни — материалы с иконками cristal_weapon / cristal_armor.
/// </summary>
[Migration(1071)]
public class SeedEnhancement : ForwardOnlyMigration
{
    public override void Up()
    {
        // INSERT OR IGNORE — миграция форвард-онли может применяться к уже
        // существующим БД, где эти строки уже есть (повторная вставка безопасна).
        Execute.Sql(@"
            INSERT OR IGNORE INTO items
                (id,name,type,value,attack,defense,max_health_bonus,heal_amount,restore_mana,stock,description,
                 bonus_strength,bonus_endurance,bonus_agility,bonus_cunning,bonus_intellect,bonus_wisdom,
                 bonus_phys_attack,bonus_mag_attack,bonus_defense,bonus_resistance,
                 bonus_attack_speed,bonus_crit_chance,bonus_crit_damage,bonus_evade_chance,
                 two_handed,damage_type,attack_speed_modifier,weapon_subtype,damage_min,damage_max,attack_range,
                 required_level,icon,quality)
            VALUES
                ('I0901','Камень усиления оружия','material',100,0,0,0,0,0,1,'Камень заточки оружия. Используется у Кузнеца для повышения характеристик оружия.',
                 0,0,0,0,0,0,0,0,0,0,0.0,0.0,0.0,0.0,0,'',1.0,'',0,0,1,0,'cristal_weapon',0)");

        Execute.Sql(@"
            INSERT OR IGNORE INTO items
                (id,name,type,value,attack,defense,max_health_bonus,heal_amount,restore_mana,stock,description,
                 bonus_strength,bonus_endurance,bonus_agility,bonus_cunning,bonus_intellect,bonus_wisdom,
                 bonus_phys_attack,bonus_mag_attack,bonus_defense,bonus_resistance,
                 bonus_attack_speed,bonus_crit_chance,bonus_crit_damage,bonus_evade_chance,
                 two_handed,damage_type,attack_speed_modifier,weapon_subtype,damage_min,damage_max,attack_range,
                 required_level,icon,quality)
            VALUES
                ('I0902','Камень усиления брони','material',100,0,0,0,0,0,1,'Камень заточки брони. Используется у Кузнеца для повышения характеристик брони и аксессуаров.',
                 0,0,0,0,0,0,0,0,0,0,0.0,0.0,0.0,0.0,0,'',1.0,'',0,0,1,0,'cristal_armor',0)");

        Execute.Sql("INSERT OR IGNORE INTO npcs (id,name,type,x,y,data) VALUES ('N0004','Кузнец','blacksmith',52,50,NULL)");
    }
}

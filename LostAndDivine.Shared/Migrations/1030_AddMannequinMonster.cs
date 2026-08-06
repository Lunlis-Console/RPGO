using FluentMigrator;

namespace LostAndDivine.Shared.Migrations;

/// <summary>
/// Манекен как обычный моб: шаблон MANNEQUIN в таблице monsters.
/// Не ходит (WanderRadius=0), не атакует (AggroRange=0), даёт 0 опыта/золота,
/// не умирает — за это отвечает флаг IsMannequin на сервере.
/// </summary>
[Migration(1030)]
public class AddMannequinMonster : ForwardOnlyMigration
{
    public override void Up()
    {
        Execute.Sql(@"INSERT OR IGNORE INTO monsters (id, name, tier, health, phys_attack, phys_defense, xp_reward, gold_reward, symbol, strength, endurance, agility, cunning, intellect, wisdom, crit_chance, crit_damage, evade_chance, block_chance, parry_chance, shield_defense)
            VALUES ('MANNEQUIN','Манекен',1,10000,0,0,0,0,'D',1,10,1,1,1,1,0.0,1.5,0.0,0.0,0.0,0)");
    }
}

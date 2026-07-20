using RPGGame.Shared.Models;
using RPGGame.Server;

namespace RPGO.Tests;

public class CombatTests
{
    private static Monster CreateMonster(int level, int str, int sta, int agi, double evade, double crit, int hp)
        => new()
        {
            Level = level, Strength = str, Stamina = sta, Agility = agi,
            EvadeChance = evade, CritChance = crit,
            Health = hp, MaxHealth = hp
        };

    private static Player CreatePlayer(int level, int str, int sta, int agi, double critChance, double evadeChance)
        => new()
        {
            Level = level, Strength = str, Stamina = sta, Agility = agi,
            BaseCritChance = critChance, BaseEvadeChance = evadeChance
        };

    [Fact]
    public void BasicHit_NoCrit_NoEvade()
    {
        var player = CreatePlayer(level: 1, str: 11, sta: 1, agi: 1, critChance: 0, evadeChance: 0);
        var monster = CreateMonster(level: 1, str: 1, sta: 1, agi: 1, evade: 0, crit: 0, hp: 100);

        var (dmgToM, dmgToP, dead, isCrit, isEvaded) =
            MonsterManager.CalculateCombat(player, monster);

        // Player total atk = 1 + (11-1)*2 = 21, monster def = 1 → 20
        Assert.Equal(20, dmgToM);
        Assert.False(isCrit);
        Assert.False(isEvaded);
        Assert.False(dead);

        // Monster total atk = 1, player def = 1 → Max(1,0) = 1
        Assert.Equal(1, dmgToP);
    }

    [Fact]
    public void PlayerCrit_DoublesDamage()
    {
        var player = CreatePlayer(level: 1, str: 11, sta: 1, agi: 1, critChance: 100, evadeChance: 0);
        var monster = CreateMonster(level: 1, str: 1, sta: 1, agi: 1, evade: 0, crit: 0, hp: 200);

        var (dmgToM, _, dead, isCrit, _) =
            MonsterManager.CalculateCombat(player, monster);

        // baseDmg = Max(1, 21-1) = 20, critDmg = 1.5+(11-1)*0.05=2.0 → 20*2.0=40
        Assert.Equal(40, dmgToM);
        Assert.True(isCrit);
        Assert.False(dead);
    }

    [Fact]
    public void MonsterEvades_NoPlayerDamage()
    {
        var player = CreatePlayer(level: 1, str: 11, sta: 1, agi: 1, critChance: 0, evadeChance: 0);
        var monster = CreateMonster(level: 1, str: 1, sta: 1, agi: 1, evade: 100, crit: 0, hp: 100);

        var (dmgToM, dmgToP, dead, isCrit, isEvaded) =
            MonsterManager.CalculateCombat(player, monster);

        // Monster evades → no player damage
        Assert.Equal(0, dmgToM);
        Assert.False(dead);
        // Monster still attacks (player didn't evade, isEvaded tracks player evade)
        Assert.False(isEvaded);
        Assert.True(dmgToP >= 1);
    }

    [Fact]
    public void PlayerEvades_NoMonsterDamage()
    {
        var player = CreatePlayer(level: 1, str: 1, sta: 1, agi: 1, critChance: 0, evadeChance: 100);
        var monster = CreateMonster(level: 1, str: 11, sta: 1, agi: 1, evade: 0, crit: 0, hp: 100);

        var (_, dmgToP, dead, _, isEvaded) =
            MonsterManager.CalculateCombat(player, monster);

        // Player hits monster
        Assert.False(dead);
        // Monster can't counter because player evades
        Assert.Equal(0, dmgToP);
        Assert.True(isEvaded);
    }

    [Fact]
    public void MonsterDies_NoRetaliation()
    {
        var player = CreatePlayer(level: 1, str: 11, sta: 1, agi: 1, critChance: 0, evadeChance: 0);
        var monster = CreateMonster(level: 1, str: 1, sta: 1, agi: 1, evade: 0, crit: 0, hp: 5);

        var (dmgToM, dmgToP, dead, _, _) =
            MonsterManager.CalculateCombat(player, monster);

        // Damage = 20 > hp=5, monster dies
        Assert.Equal(20, dmgToM);
        Assert.True(dead);
        // No counter-attack
        Assert.Equal(0, dmgToP);
    }

    [Fact]
    public void MinimumDamage_AlwaysAtLeast1()
    {
        var player = CreatePlayer(level: 1, str: 1, sta: 1, agi: 1, critChance: 0, evadeChance: 0);
        var monster = CreateMonster(level: 1, str: 1, sta: 1, agi: 1, evade: 0, crit: 0, hp: 100);
        monster.Stamina = 100; // high defense

        var (dmgToM, _, _, _, _) =
            MonsterManager.CalculateCombat(player, monster);

        // playerAtk=1, monsterDef=1+(100-1)*1=100 → Max(1, 1-100)=1
        Assert.True(dmgToM >= 1);
    }

    [Fact]
    public void MonsterCrit_IncreasesDamage()
    {
        var player = CreatePlayer(level: 1, str: 1, sta: 1, agi: 1, critChance: 0, evadeChance: 0);
        var monster = CreateMonster(level: 1, str: 11, sta: 1, agi: 1, evade: 0, crit: 100, hp: 100);

        var (_, dmgToP, _, _, _) =
            MonsterManager.CalculateCombat(player, monster);

        // Monster atk = 1+(11-1)*2=21, player def=1 → baseDmg=20
        // critDmg = 1.5+(11-1)*0.05=2.0 → 20*2.0=40
        Assert.Equal(40, dmgToP);
    }

    [Fact]
    public void HighLevelPlayer_HighLevelMonster_FairFight()
    {
        var player = CreatePlayer(level: 10, str: 10, sta: 10, agi: 1, critChance: 0, evadeChance: 0);
        var monster = CreateMonster(level: 10, str: 10, sta: 10, agi: 1, evade: 0, crit: 0, hp: 1000);

        var (dmgToM, dmgToP, dead, _, _) =
            MonsterManager.CalculateCombat(player, monster);

        // Player: BaseDmg=10 + (10-1)*2=18 = 28. Monster def: 10+(10-1)*1=19. → 9
        Assert.Equal(9, dmgToM);
        Assert.False(dead);

        // Monster: BaseDmg=10 + (10-1)*2=28. Player def: 10+(10-1)*1=19. → 9
        Assert.Equal(9, dmgToP);
    }

    [Fact]
    public void PlayerVsPlayer_UsesICombatant()
    {
        // Фундамент PvP: CalculateCombat теперь принимает любой ICombatant.
        // Игрок-агрессор бьёт другого игрока (цель не мутируется здесь —
        // применение урона к игроку добавится в PvP-цикле позже).
        var attacker = CreatePlayer(level: 5, str: 11, sta: 1, agi: 1, critChance: 0, evadeChance: 0);
        var defender = CreatePlayer(level: 5, str: 1, sta: 1, agi: 1, critChance: 0, evadeChance: 0);

        var (dmgToDefender, dmgToAttacker, dead, _, _) =
            MonsterManager.CalculateCombat(attacker, defender);

        // Attacker atk = 1 + (11-1)*2 = 21, defender def = 1 → 20
        Assert.Equal(20, dmgToDefender);
        // Defender atk = 1, attacker def = 1 → 1 (counter)
        Assert.Equal(1, dmgToAttacker);
        Assert.False(dead);
        // Защитник-игрок не получает урон напрямую через этот метод (PvP-применение — отдельно)
        Assert.Equal(100, defender.Health);
    }
}

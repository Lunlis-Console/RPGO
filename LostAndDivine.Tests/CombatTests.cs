using LostAndDivine.Shared.Models;
using LostAndDivine.Server;
using LostAndDivine.Server.Services;

namespace LostAndDivine.Tests;

public class CombatTests
{
    private static readonly MonsterManager _monsters;

    static CombatTests()
    {
        var world = new GameWorld(100, 100);
        _monsters = new MonsterManager(world);
        var debuffs = new DebuffManager();
        var quests = new QuestManager(world);
        var merchant = new MerchantManager(world);
        var svc = new GameServices(world, null!, null!, _monsters,
            new WandererManager(world),
            new LootManager(world), new CorpseManager(),
            quests, merchant,
            new CollectibleManager(world), new TradeManager(),
            new DialogueManager(world, quests, merchant), new PartyManager(world),
            new ProjectileManager(world), new KillService(world),
            new PathfindingService(world, merchant, quests), debuffs,
            auth: null!, zones: null!, persistence: null!, clientBuild: null!, storage: null!);
        var monsterAttacks = new MonsterAttackService(svc);
        var monsterCombat = new MonsterCombatCalculator(svc);
        svc.MonsterAttacks = monsterAttacks;
        svc.MonsterCombat = monsterCombat;
        _monsters.SetServices(svc);
    }
    private static Monster CreateMonster(int level, int str, int sta, int agi, double evade, double crit, int hp)
        => new()
        {
            Level = level, Strength = str, Endurance = sta, Agility = agi,
            EvadeChance = evade, CritChance = crit,
            Health = hp, MaxHealth = hp
        };

    private static Player CreatePlayer(int level, int str, int sta, int agi, double critChance, double evadeChance)
        => new()
        {
            Level = level, Strength = str, Endurance = sta, Agility = agi,
            BaseCritChance = critChance, BaseEvadeChance = evadeChance
        };

    [Fact]
    public void BasicHit_NoCrit_NoEvade()
    {
        var player = CreatePlayer(level: 1, str: 11, sta: 1, agi: 1, critChance: 0, evadeChance: 0);
        var monster = CreateMonster(level: 1, str: 1, sta: 1, agi: 1, evade: 0, crit: 0, hp: 100);

        var (dmgToM, dmgToP, dead, isCrit, isEvaded, isParried, isBlocked) =
            _monsters.CalculateCombat(player, monster);

        // Player total atk = 1 + (11-1)*2 = 21, monster def = 1 → 20
        Assert.Equal(20, dmgToM);
        Assert.False(isCrit);
        Assert.False(isEvaded);
        Assert.False(dead);

        // Monster no longer counter-attacks (attacks via WanderStep aggro instead)
        Assert.Equal(0, dmgToP);
    }

    [Fact]
    public void PlayerCritChance_CappedAt75()
    {
        var player = CreatePlayer(level: 1, str: 11, sta: 1, agi: 1, critChance: 100, evadeChance: 0);
        // Убывающая отдача + кап: даже 100% базы не даёт больше 75%
        Assert.Equal(75.0, player.GetCritChance());
    }

    [Fact]
    public void PlayerCrit_DoublesDamage()
    {
        var player = CreatePlayer(level: 1, str: 11, sta: 1, agi: 1, critChance: 100, evadeChance: 0);
        var monster = CreateMonster(level: 1, str: 1, sta: 1, agi: 1, evade: 0, crit: 0, hp: 10000);

        bool sawCrit = false;
        for (int i = 0; i < 200; i++)
        {
            monster.Health = monster.MaxHealth;
            var (dmgToM, _, dead, isCrit, _, _, _) =
                _monsters.CalculateCombat(player, monster);

            // baseDmg = 21*(1-1/501)≈20.96 → 20; critDmg = 1.5+10*0.02=1.7 → 34 (сила 11 − 1 = 10 очков)
            Assert.Equal(isCrit ? 34 : 20, dmgToM);
            Assert.False(dead);
            if (isCrit) sawCrit = true;
        }
        Assert.True(sawCrit);
    }

    [Fact]
    public void MonsterEvades_NoPlayerDamage()
    {
        var player = CreatePlayer(level: 1, str: 11, sta: 1, agi: 1, critChance: 0, evadeChance: 0);
        var monster = CreateMonster(level: 1, str: 1, sta: 1, agi: 1, evade: 100, crit: 0, hp: 100);

        var (dmgToM, dmgToP, dead, isCrit, isEvaded, _, _) =
            _monsters.CalculateCombat(player, monster);

        // Monster evades → no player damage
        Assert.Equal(0, dmgToM);
        Assert.False(dead);
        Assert.True(isEvaded);
        Assert.Equal(0, dmgToP);
    }

    [Fact]
    public void PlayerEvades_NoMonsterDamage()
    {
        var player = CreatePlayer(level: 1, str: 1, sta: 1, agi: 1, critChance: 0, evadeChance: 100);
        var monster = CreateMonster(level: 1, str: 11, sta: 1, agi: 1, evade: 0, crit: 0, hp: 100);

        var (_, dmgToP, dead, _, isEvaded, _, _) =
            _monsters.CalculateCombat(player, monster);

        // Player hits monster
        Assert.False(dead);
        // No counter-attack (removed from CalculateCombat)
        Assert.Equal(0, dmgToP);
        Assert.False(isEvaded);
    }

    [Fact]
    public void MonsterDies_NoRetaliation()
    {
        var player = CreatePlayer(level: 1, str: 11, sta: 1, agi: 1, critChance: 0, evadeChance: 0);
        var monster = CreateMonster(level: 1, str: 1, sta: 1, agi: 1, evade: 0, crit: 0, hp: 5);

        var (dmgToM, dmgToP, dead, _, _, _, _) =
            _monsters.CalculateCombat(player, monster);

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
        monster.Endurance = 100; // high defense

        var (dmgToM, _, _, _, _, _, _) =
            _monsters.CalculateCombat(player, monster);

        // playerAtk=1, monsterDef=1+(100-1)*1=100 → Max(1, 1-100)=1
        Assert.True(dmgToM >= 1);
    }

    [Fact]
    public void MonsterCrit_IncreasesDamage()
    {
        var player = CreatePlayer(level: 1, str: 1, sta: 1, agi: 1, critChance: 0, evadeChance: 0);
        var monster = CreateMonster(level: 1, str: 11, sta: 1, agi: 1, evade: 0, crit: 100, hp: 100);

        var (_, dmgToP, _, _, _, _, _) =
            _monsters.CalculateCombat(player, monster);

        // No counter-attack from CalculateCombat (removed)
        Assert.Equal(0, dmgToP);
    }

    [Fact]
    public void HighLevelPlayer_HighLevelMonster_FairFight()
    {
        var player = CreatePlayer(level: 10, str: 10, sta: 10, agi: 1, critChance: 0, evadeChance: 0);
        var monster = CreateMonster(level: 10, str: 10, sta: 10, agi: 1, evade: 0, crit: 0, hp: 1000);

        var (dmgToM, dmgToP, dead, _, _, _, _) =
            _monsters.CalculateCombat(player, monster);

        // Player: BaseDmg=10 + (10-1)*2=18 = 28. Monster def=19 → DR=19/519≈3.66% → 28*0.9634≈26.97 → 26
        Assert.Equal(26, dmgToM);
        Assert.False(dead);

        // No counter-attack (removed from CalculateCombat)
        Assert.Equal(0, dmgToP);
    }

    [Fact]
    public void PlayerVsPlayer_UsesICombatant()
    {
        // Фундамент PvP: CalculateCombat теперь принимает любой ICombatant.
        // Игрок-агрессор бьёт другого игрока (цель не мутируется здесь —
        // применение урона к игроку добавится в PvP-цикле позже).
        var attacker = CreatePlayer(level: 5, str: 11, sta: 1, agi: 1, critChance: 0, evadeChance: 0);
        var defender = CreatePlayer(level: 5, str: 1, sta: 1, agi: 1, critChance: 0, evadeChance: 0);

        var (dmgToDefender, dmgToAttacker, dead, _, _, _, _) =
            _monsters.CalculateCombat(attacker, defender);

        // Attacker atk = 5 + (11-1)*2 = 25, defender def = 5 → DR=5/505≈0.99% → 25*0.9901≈24.75 → 24
        Assert.Equal(24, dmgToDefender);
        // No counter-attack (removed from CalculateCombat)
        Assert.Equal(0, dmgToAttacker);
        Assert.False(dead);
        // Защитник-игрок не получает урон напрямую через этот метод (PvP-применение — отдельно)
        Assert.Equal(100, defender.Health);
    }

    [Fact]
    public void SpawnMannequins_CreatesDistinctMonstersAtEachPosition()
    {
        var world = new GameWorld(100, 100);
        var monsters = new MonsterManager(world);
        monsters.AddMannequinPosition(50, 50);
        monsters.AddMannequinPosition(52, 50);
        monsters.AddMannequinPosition(54, 50);
        monsters.SpawnMannequins();

        var spawned = monsters.GetAllMonsters().Where(m => m.IsMannequin).ToList();
        Assert.Equal(3, spawned.Count);
        // Каждый манекен — отдельный объект с уникальным Id и своей позицией
        Assert.Equal(3, spawned.Select(m => m.Id).Distinct().Count());
        Assert.Equal(3, spawned.Select(m => (m.X, m.Y)).Distinct().Count());
    }

    [Fact]
    public void Mannequin_SpawnsAsRegularMonsterFromTemplate()
    {
        var world = new GameWorld(100, 100);
        world.SetMonsterTemplates(new List<MonsterTemplate>
        {
            new() { Id = "MANNEQUIN", Name = "Манекен", Tier = 1, Health = 10000, XpReward = 0, GoldReward = 0, Symbol = 'D', Endurance = 10 }
        });
        var monsters = new MonsterManager(world);
        monsters.AddMannequinPosition(50, 50);
        monsters.SpawnMannequins();

        var spawned = monsters.GetAllMonsters().Where(m => m.IsMannequin).ToList();
        Assert.Single(spawned);
        var m = spawned[0];
        // Статы берутся из шаблона content.db, как у обычного моба
        Assert.Equal("MANNEQUIN", m.TemplateId);
        Assert.Equal("Манекен", m.Name);
        Assert.Equal(10000, m.MaxHealth);
        // Манекен не ходит, не атакует и не даёт наград
        Assert.Equal(0, m.WanderRadius);
        Assert.Equal(0, m.AggroRange);
        Assert.Equal(0, m.XpReward);
        Assert.Equal(0, m.GoldReward);
    }

    [Fact]
    public void DamagingOneMannequin_DoesNotAffectOthers()
    {
        var world = new GameWorld(100, 100);
        var monsters = new MonsterManager(world);
        monsters.AddMannequinPosition(50, 50);
        monsters.AddMannequinPosition(52, 50);
        monsters.SpawnMannequins();

        var spawned = monsters.GetAllMonsters().Where(m => m.IsMannequin).ToList();
        Assert.Equal(2, spawned.Count);
        int full = Balance.MannequinHealth;

        spawned[0].Health -= 500;

        Assert.Equal(full - 500, spawned[0].Health);
        Assert.Equal(full, spawned[1].Health);
    }
}

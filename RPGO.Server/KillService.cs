using RPGGame.Shared.Models;
using RPGGame.Shared.Network;
using RPGGame.Server.Network;

namespace RPGGame.Server;

/// <summary>
/// Обработка убийств монстров:分配经验, лут, квесты.
/// Зависимости инжектируются через конструктор вместо Program.Services.
/// </summary>
public class KillService
{
    private readonly GameWorld _world;
    private GameServices _svc = null!;
    private INetworkHub? _hub;

    public KillService(GameWorld world)
    {
        _world = world;
    }

    public void SetHub(INetworkHub hub) => _hub = hub;
    public void SetGameServices(GameServices svc) => _svc = svc;

    private Task ChatTo(ClientConnection? conn, ChatChannel channel, string name, string text)
    {
        if (conn == null || _hub == null) return Task.CompletedTask;
        return _hub.SendChatToAsync(conn, channel, name, text);
    }

    public async Task ResolveMonsterKill(
        Player killer,
        Monster monster,
        int totalDamageDealt,
        bool sendDamageMsg,
        GameMessage? damageMsg,
        bool isProjectile = false)
    {
        if (_hub == null) return;
        var client = _world.FindClientByPlayer(killer);
        if (client == null) return;

        killer.Combat.Cancel();
        killer.Combat.OffHandLastAttackTime = DateTime.MinValue;

        int shownDmg = Math.Max(0, monster.Health + totalDamageDealt);
        string source = isProjectile ? "снарядом" : "";
        string sourcePrefix = isProjectile ? "Снаряд " : "";
        Log.Info($"{killer.Name} убил {monster.Name} {source}!");
        await ChatTo(client, ChatChannel.Combat, "Бой",
            $"{sourcePrefix}Вы нанесли {shownDmg} урона и убили {monster.Name}!");

        if (sendDamageMsg && damageMsg != null)
        {
            await _hub.SendToClient(client, damageMsg);
            await _hub.SendDamageNearbyAsync(monster.X, monster.Y, damageMsg, killer);
        }

        await _hub.SendToClient(client, GameMessage.ResetCombat());

        var damageTracker = monster.DamageTracker;
        var partyContributors = new List<(Player Player, int Damage)>();
        bool isPartyMode = false;

        if (killer.PartyId.HasValue)
        {
            var party = _svc.Party.GetParty(killer.PartyId.Value);
            if (party != null)
            {
                foreach (var kvp in damageTracker)
                {
                    if (_world.TryGetPlayer(kvp.Key, out var contributor) && contributor != null
                        && contributor.PartyId == killer.PartyId)
                    {
                        partyContributors.Add((contributor, kvp.Value));
                    }
                }
                if (partyContributors.Count > 1)
                    isPartyMode = true;
            }
        }

        int totalDamage = damageTracker.Values.Sum();

        if (isPartyMode)
            await ResolvePartyKill(killer, monster, partyContributors, totalDamage);
        else
            await ResolveSoloKill(killer, monster, damageTracker);
    }

    private async Task ResolveSoloKill(
        Player killer,
        Monster monster,
        Dictionary<Guid, int> damageTracker)
    {
        if (_hub == null) return;
        var topContributor = damageTracker.OrderByDescending(kvp => kvp.Value).FirstOrDefault();
        Player soloRecipient = topContributor.Key != Guid.Empty
            && _world.TryGetPlayer(topContributor.Key, out var topP) && topP != null
            ? topContributor.Value > 0 ? topP : killer
            : killer;

        soloRecipient.Experience += monster.XpReward;
        if (soloRecipient.TryLevelUp()) Log.Info($"{soloRecipient.Name} повысил уровень до {soloRecipient.Level}!");

        var soloLoot = _svc.Loot.RollLoot(monster.TemplateId);
        var soloPlayerLoot = new Dictionary<Guid, CorpsePlayerLoot>
        {
            [soloRecipient.Id] = new CorpsePlayerLoot
            {
                PlayerName = soloRecipient.Name,
                Gold = monster.GoldReward,
                Items = soloLoot,
                DamagePercent = 100
            }
        };

        _svc.Corpses.CreateCorpse(monster, new List<Item>(), soloPlayerLoot);
        _svc.Monsters.RemoveMonster(monster);

        var soloClient = _world.FindClientByPlayer(soloRecipient);
        if (soloClient != null)
        {
            int totalItems = soloLoot.Count;
            if (totalItems > 0 || monster.GoldReward > 0)
                await _hub.SendToClient(soloClient, GameMessage.Chat("Система",
                    $"Тело {monster.Name} осталось на земле. Нажмите, чтобы забрать дроп ({totalItems} предм., {monster.GoldReward} зол.)."));
            else
                await _hub.SendToClient(soloClient, GameMessage.Chat("Система",
                    $"Тело {monster.Name} осталось на земле. Дропа нет."));

            await SendQuestUpdates(soloClient, soloRecipient, monster);
        }
    }

    private async Task ResolvePartyKill(
        Player killer,
        Monster monster,
        List<(Player Player, int Damage)> partyContributors,
        int totalDamage)
    {
        if (_hub == null) return;
        var playerLootDict = new Dictionary<Guid, CorpsePlayerLoot>();

        foreach (var (contributor, dmg) in partyContributors)
        {
            double dmgShare = totalDamage > 0 ? (double)dmg / totalDamage : 0;
            int xpReward = (int)(monster.XpReward * dmgShare);
            int goldReward = (int)(monster.GoldReward * dmgShare);

            contributor.Experience += xpReward;
            if (contributor.TryLevelUp()) Log.Info($"{contributor.Name} повысил уровень до {contributor.Level}!");

            var contributorLoot = _svc.Loot.RollLoot(monster.TemplateId);
            playerLootDict[contributor.Id] = new CorpsePlayerLoot
            {
                PlayerName = contributor.Name,
                Gold = goldReward,
                Items = contributorLoot,
                DamagePercent = (int)(dmgShare * 100)
            };

            var contribClient = _world.FindClientByPlayer(contributor);
            if (contribClient != null)
            {
                if (xpReward > 0)
                    await ChatTo(contribClient, ChatChannel.System, "Система",
                        $"[Группа] Вы получили {xpReward} опыта за {monster.Name} ({(int)(dmgShare * 100)}% урона).");

                int personalItems = contributorLoot.Count;
                if (personalItems > 0 || goldReward > 0)
                    await ChatTo(contribClient, ChatChannel.System, "Система",
                        $"Тело {monster.Name} осталось на земле. Нажмите, чтобы забрать дроп ({personalItems} предм., {goldReward} зол.).");
                else
                    await ChatTo(contribClient, ChatChannel.System, "Система",
                        $"Тело {monster.Name} осталось на земле. Дропа нет.");

                await SendQuestUpdates(contribClient, contributor, monster);
            }
        }

        _svc.Corpses.CreateCorpse(monster, new List<Item>(), playerLootDict);
        _svc.Monsters.RemoveMonster(monster);
    }

    private async Task SendQuestUpdates(ClientConnection client, Player player, Monster monster)
    {
        if (_hub == null) return;
        var questResults = _svc.Quests.IncrementKillProgress(player, monster.TemplateId);
        foreach (var (title, current, target, completed) in questResults)
        {
            string msg = completed
                ? $"[Задание] {title}: {current}/{target} — задание выполнено! Вернитесь на доску заданий, чтобы сдать."
                : $"[Задание] {title}: {current}/{target}";
            await ChatTo(client, ChatChannel.System, "Система", msg);
        }
        await _hub.SendQuestLog(client, player);
    }
}

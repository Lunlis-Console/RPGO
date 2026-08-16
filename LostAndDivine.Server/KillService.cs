using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;
using LostAndDivine.Server.Network;

namespace LostAndDivine.Server;

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

        // Обработка инстанс-монстров
        if (monster.ZoneId.StartsWith("instance:"))
        {
            await ResolveInstanceMonsterKill(killer, monster);
            return;
        }

        var damageTracker = monster.DamageTracker;
        var partyContributors = new List<(Player Player, int Damage)>();
        bool isPartyMode = false;

        if (killer.PartyId.HasValue)
        {
            var party = _svc.Party.GetParty(killer.PartyId.Value);
            if (party != null)
            {
                // Участники — все члены группы в той же зоне, что и убийство,
                // независимо от нанесённого урона.
                foreach (var memberId in party.Members)
                {
                    if (_world.TryGetPlayer(memberId, out var member) && member != null
                        && member.CurrentZoneId == monster.ZoneId)
                    {
                        int dmg = damageTracker.TryGetValue(member.Id, out var d) ? d : 0;
                        partyContributors.Add((member, dmg));
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

    private async Task ResolveInstanceMonsterKill(Player killer, Monster monster)
    {
        bool isBoss = _svc.Instances.IsBossMonster(monster);

        if (isBoss)
        {
            // Босс: награда и уведомление — всем участникам в инстансе (поровну)
            var participants = _svc.Instances.GetPlayersInZone(monster.ZoneId);
            if (participants.Count == 0) participants.Add(killer);
            int count = participants.Count;
            int xpShare = monster.XpReward / count;
            int goldShare = monster.GoldReward / count;
            int sharePercent = 100 / count;

            foreach (var p in participants)
            {
                p.Experience += xpShare;
                p.Gold += goldShare;
                if (p.TryLevelUp()) Log.Info($"{p.Name} повысил уровень до {p.Level}!");

                var pc = _world.FindClientByPlayer(p);
                if (pc == null) continue;
                await _svc.Hub.SendStatusAsync(pc, p);
                if (count > 1)
                    await _svc.Hub.SendChatToAsync(pc, ChatChannel.System, "Система",
                        $"Вы получили {xpShare} опыта и {goldShare} золота за убийство {monster.Name} ({sharePercent}% доли группы).");
                else
                    await _svc.Hub.SendChatToAsync(pc, ChatChannel.System, "Система",
                        $"Вы получили {monster.XpReward} опыта и {monster.GoldReward} золота за убийство {monster.Name}.");
                await _svc.Hub.SendChatToAsync(pc, ChatChannel.System, "Система",
                    "Босс повержен! Сундук разблокирован. У вас есть время забрать лут до закрытия инстанса.");
            }

            _svc.Instances.OnBossKilled(monster.ZoneId);
        }
        else
        {
            killer.Experience += monster.XpReward;
            if (killer.TryLevelUp()) Log.Info($"{killer.Name} повысил уровень до {killer.Level}!");

            var client = _world.FindClientByPlayer(killer);
            if (client != null)
            {
                await _svc.Hub.SendStatusAsync(client, killer);
                int goldReward = monster.GoldReward;
                killer.Gold += goldReward;
                await _svc.Hub.SendChatToAsync(client, ChatChannel.System, "Система",
                    $"Вы получили {monster.XpReward} опыта и {goldReward} золота за убийство {monster.Name}.");
            }
        }

        _svc.Instances.RemoveMonster(monster);

        await _svc.Hub.BroadcastMapAsync();
    }

    private async Task ResolveSoloKill(
        Player killer,
        Monster monster,
        IDictionary<Guid, int> damageTracker)
    {
        if (_hub == null) return;
        var topContributor = damageTracker.OrderByDescending(kvp => kvp.Value).FirstOrDefault();
        Player soloRecipient = topContributor.Key != Guid.Empty
            && _world.TryGetPlayer(topContributor.Key, out var topP) && topP != null
            ? topContributor.Value > 0 ? topP : killer
            : killer;

        soloRecipient.Experience += monster.XpReward;
        if (soloRecipient.TryLevelUp()) Log.Info($"{soloRecipient.Name} повысил уровень до {soloRecipient.Level}!");

        var soloClient = _world.FindClientByPlayer(soloRecipient);
        if (soloClient != null)
            await _svc.Hub.SendStatusAsync(soloClient, soloRecipient);

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
        var participants = partyContributors.Select(c => c.Player).ToList();
        int count = participants.Count;
        if (count == 0) return;

        // Дроп и опыт делятся поровну между участниками группы —
        // вне зависимости от нанесённого урона. Если опыт/золото не делятся
        // нацело, каждый получает целую часть поровну (остаток не выдаётся).
        int xpReward = monster.XpReward / count;
        int goldReward = monster.GoldReward / count;

        var allLoot = _svc.Loot.RollLoot(monster.TemplateId);
        // Раскладываем предметы по игрокам по кругу — равные доли.
        var lootByIndex = new Dictionary<int, List<Item>>();
        for (int i = 0; i < count; i++) lootByIndex[i] = new List<Item>();
        for (int i = 0; i < allLoot.Count; i++)
            lootByIndex[i % count].Add(allLoot[i]);

        int sharePercent = 100 / count;
        var playerLootDict = new Dictionary<Guid, CorpsePlayerLoot>();

        for (int i = 0; i < participants.Count; i++)
        {
            var contributor = participants[i];

            contributor.Experience += xpReward;
            if (contributor.TryLevelUp()) Log.Info($"{contributor.Name} повысил уровень до {contributor.Level}!");

            var contribClient = _world.FindClientByPlayer(contributor);
            if (contribClient != null)
                await _svc.Hub.SendStatusAsync(contribClient, contributor);

            playerLootDict[contributor.Id] = new CorpsePlayerLoot
            {
                PlayerName = contributor.Name,
                Gold = goldReward,
                Items = lootByIndex[i],
                DamagePercent = sharePercent
            };

            if (contribClient != null)
            {
                if (xpReward > 0)
                    await ChatTo(contribClient, ChatChannel.System, "Система",
                        $"[Группа] Вы получили {xpReward} опыта за {monster.Name} ({sharePercent}% доли группы).");

                int personalItems = lootByIndex[i].Count;
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
        _hub.MarkZoneDirty(player.CurrentZoneId);
        await _hub.BroadcastMapAsync();
    }
}

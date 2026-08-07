using LostAndDivine.Server.Network;
using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;

namespace LostAndDivine.Server;

public class MonsterAttackService
{
    private readonly IGameServices _svc;

    public MonsterAttackService(IGameServices svc)
    {
        _svc = svc;
    }

    public async Task MonsterAttackTick()
    {
        var attacks = _svc.Monsters.DrainPendingAttacks();
        foreach (var (monster, player, damage) in attacks)
        {
            if (player.IsDead) continue;

            double accuracyReduction = _svc.Debuffs.GetDebuffValue(monster, DebuffType.AccuracyReduction);
            double evadeChance = player.GetEvadeChance() + accuracyReduction * 100 + player.GetMeleeEvadeBonus();
            bool evaded = Balance.RollPercent(evadeChance);
            bool parried = !evaded && Balance.RollPercent(player.GetParryChance());
            bool blocked = !evaded && !parried && Balance.RollPercent(player.GetBlockChance());
            int finalDmg = (evaded || parried || blocked) ? 0 : damage;

            player.Health -= finalDmg;
            player.LastDamagedTime = DateTime.UtcNow;
            var client = _svc.World.FindClientByPlayer(player);
            if (client == null) continue;

            if (evaded)
            {
                var missMsg = GameMessage.Damage("player", null, player.X, player.Y, 0, false, player.Name, result: "miss");
                await _svc.Hub.SendToClient(client, missMsg);
                await _svc.ChatTo(client, ChatChannel.Combat, "Бой", $"Вы уклонились от атаки {monster.Name}.");
            }
            else if (parried)
            {
                var parryMsg = GameMessage.Damage("player", null, player.X, player.Y, 0, false, player.Name, result: "parry");
                await _svc.Hub.SendToClient(client, parryMsg);
                await _svc.ChatTo(client, ChatChannel.Combat, "Бой", $"Вы парировали атаку {monster.Name}!");
            }
            else if (blocked)
            {
                var blockMsg = GameMessage.Damage("player", null, player.X, player.Y, 0, false, player.Name, result: "block");
                await _svc.Hub.SendToClient(client, blockMsg);
                await _svc.ChatTo(client, ChatChannel.Combat, "Бой", $"Вы заблокировали атаку {monster.Name}!");
            }
            else
            {
                var hitMsg = GameMessage.Damage("player", null, player.X, player.Y, finalDmg, false, player.Name);
                await _svc.Hub.SendToClient(client, hitMsg);
                await _svc.Hub.SendDamageNearbyAsync(player.X, player.Y, hitMsg, player);
                await _svc.ChatTo(client, ChatChannel.Combat, "Бой", $"{monster.Name} нанёс вам {finalDmg} урона. ({player.Health}/{player.MaxHealth + player.Equipment.GetBonusMaxHealth()}) HP");
            }

            await _svc.Hub.SendToClient(client, GameMessage.CombatUpdate(monster.Name, monster.Health, monster.MaxHealth));
            await _svc.Party.SendUpdateForAsync(player);

            if (player.Health <= 0)
            {
                int lostGold = Balance.ComputeDeathGoldLoss(player.Gold);
                player.Gold -= lostGold;
                player.Combat.Cancel();
                player.Interaction.Clear();
                player.Movement.Stop();
                player.IsDead = true;
                player.DeathTime = DateTime.UtcNow;
                Log.Info($"{player.Name} погиб от {monster.Name}! Потеряно {lostGold} золота. Таймер 5с.");
                await _svc.ChatTo(client, ChatChannel.System, "Система", $"Вы погибли от {monster.Name}! Потеряно {lostGold} золота. Возрождение через 5 сек...");
                await _svc.Hub.SendToClient(client, GameMessage.ResetCombat());
                await _svc.Hub.SendToClient(client, GameMessage.PlayerDeath(lostGold));
                await _svc.Party.SendUpdateForAsync(player);
            }

            await _svc.Hub.SendStatusAsync(client, player);
            _svc.Hub.MarkZoneDirty(player.CurrentZoneId);
        }
    }
}

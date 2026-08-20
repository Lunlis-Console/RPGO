using LostAndDivine.Server.Network;
using LostAndDivine.Server.Services;
using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;

namespace LostAndDivine.Server.MessageHandlers;

/// <summary>
/// Базовый класс хендлеров: даёт доступ к GameServices, GameWorld и сетевому хабу.
/// </summary>
public abstract class BaseHandler : IMessageHandler
{
    protected GameServices Svc { get; }
    protected GameWorld World => Svc.World;
    protected INetworkHub Hub => Svc.Hub;

    protected BaseHandler(GameServices svc)
    {
        Svc = svc;
    }

    public abstract Task Handle(ClientConnection connection, GameMessage message, Player? player);

    protected Task SendToClient(ClientConnection connection, GameMessage message)
        => Hub.SendToClient(connection, message);

    protected Task BroadcastMapAsync()
        => Hub.BroadcastMapAsync();

    protected Task BroadcastChatAsync(string name, string text)
        => Hub.BroadcastChatAsync(name, text);

    protected Task BroadcastChatAsync(ChatChannel channel, string from, string text)
        => Hub.BroadcastChatAsync(channel, from, text);

    protected Task SendChatToAsync(ClientConnection connection, ChatChannel channel, string from, string text, string? to = null)
        => Hub.SendChatToAsync(connection, channel, from, text, to);

    protected Task SendSystem(ClientConnection connection, string msg)
        => Hub.SendChatToAsync(connection, ChatChannel.System, "Система", msg);

    protected async Task SendChatLocalAsync(Player sender, ChatChannel channel, string from, string text)
    {
        int view = World.Map.ViewRadius;
        foreach (var p in World.GetPlayersSnapshot())
        {
            if (Math.Abs(p.X - sender.X) > view || Math.Abs(p.Y - sender.Y) > view) continue;
            var conn = World.FindClientByPlayer(p);
            if (conn != null) await SendChatToAsync(conn, channel, from, text);
        }
    }

    protected async Task SendChatPartyAsync(Player sender, string from, string text)
    {
        var party = Svc.Party.GetPartyForPlayer(sender.Id);
        var targets = new List<Player>();
        if (party != null)
        {
            foreach (var memberId in party.Members)
            {
                var pl = World.GetPlayersSnapshot().FirstOrDefault(x => x.Id == memberId);
                if (pl != null) targets.Add(pl);
            }
        }
        if (targets.Count == 0) targets.Add(sender);

        foreach (var pl in targets)
        {
            var conn = World.FindClientByPlayer(pl);
            if (conn != null) await SendChatToAsync(conn, ChatChannel.Party, from, text);
        }
    }

    protected async Task SendWhisperAsync(Player from, string toName, string text)
    {
        if (!World.TryGetPlayerByName(toName, out var target))
        {
            var self = World.FindClientByPlayer(from);
            if (self != null)
                await SendChatToAsync(self, ChatChannel.System, "Система",
                    $"Игрок «{toName}» не найден или не в сети.");
            return;
        }

        var fromConn = World.FindClientByPlayer(from);
        var toConn = World.FindClientByPlayer(target!);
        if (toConn != null)
            await SendChatToAsync(toConn, ChatChannel.Whisper, from.Name, text, to: target!.Name);
        if (fromConn != null)
            await SendChatToAsync(fromConn, ChatChannel.Whisper, from.Name, text, to: target!.Name);
    }

    protected Task ReloadContent(ClientConnection? connection = null)
        => Svc.ReloadContent(connection);

    protected double GetAttackSpeed(Player player)
        => Math.Min(Balance.MaxAttackSpeed,
            Balance.GetAttackSpeedWithWeapon(player.GetAttackSpeedPoints(), player.Equipment.GetWeaponSpeedModifier())
            * player.GetAttackSpeedGearMultiplier());

    protected StatsBreakdown BuildBreakdown(Player player)
        => Hub.BuildBreakdown(player);

    protected Task SendInventoryAndStatus(ClientConnection connection, Player player, bool fromUnequip = false)
        => Hub.SendInventoryAndStatus(connection, player, fromUnequip);

    protected static object MakeItemPayload(Item i) => new
    {
        i.Id, i.TemplateId, i.Name, i.Type, i.WeaponSubtype, i.Quantity, i.Value,
        i.MaxHealthBonus, i.MaxManaBonus, i.HealAmount, i.RestoreMana, i.Description, i.MaxStack,
        BonusStrength = i.BonusStrength, BonusEndurance = i.BonusEndurance,
        BonusAgility = i.BonusAgility, BonusCunning = i.BonusCunning,
        BonusIntellect = i.BonusIntellect, BonusWisdom = i.BonusWisdom,
        BonusPhysAttack = i.BonusPhysAttack, BonusMagAttack = i.BonusMagAttack,
        BonusDefense = i.BonusDefense, BonusResistance = i.BonusResistance,
        i.Defense, i.MagicDefense,
        BonusCritChance = i.BonusCritChance, BonusCritDamage = i.BonusCritDamage,
        BonusEvadeChance = i.BonusEvadeChance, BonusAttackSpeed = i.BonusAttackSpeed,
        BonusBlockChance = i.BonusBlockChance, BonusParryChance = i.BonusParryChance,
        BonusAccuracy = i.BonusAccuracy, BonusTenacity = i.BonusTenacity,
        BonusArmorPenetration = i.BonusArmorPenetration, BonusCooldownReduction = i.BonusCooldownReduction,
        BonusHpRegen = i.BonusHpRegen, BonusMpRegen = i.BonusMpRegen,
        i.DamageType, i.RequiredLevel, i.DamageMin, i.DamageMax,
        i.AttackSpeedModifier, i.TwoHanded, i.AttackRange, i.Icon
    };

    protected Task SendQuestLog(ClientConnection connection, Player player)
        => Hub.SendQuestLog(connection, player);

    protected Task ProcessPendingInteraction(Player player, string interactionType)
        => Svc.Interactions.ProcessPendingInteraction(player, interactionType);

    protected Task SendError(ClientConnection connection, string code, string message)
        => Hub.SendError(connection, code, message);
}

using RPGGame.Shared.Models;

namespace RPGGame.Server;

public class TradeManager
{
    private static readonly Dictionary<Guid, TradeSession> _sessionsByPlayer = new();
    private static readonly object _lock = new();

    public static TradeSession? GetSession(Guid playerId)
    {
        lock (_lock)
        {
            _sessionsByPlayer.TryGetValue(playerId, out var session);
            return session;
        }
    }

    public static TradeSession? GetSessionByPlayer(Player player)
    {
        return GetSession(player.Id);
    }

    public static bool IsInTrade(Player player)
    {
        lock (_lock) { return _sessionsByPlayer.ContainsKey(player.Id); }
    }

    public static TradeSession CreateSession(Player initiator, Player partner)
    {
        var session = new TradeSession(initiator, partner);
        lock (_lock)
        {
            _sessionsByPlayer[initiator.Id] = session;
            _sessionsByPlayer[partner.Id] = session;
        }
        Log.Info($"Трейд создан: {initiator.Name} ↔ {partner.Name}");
        return session;
    }

    public static void RemoveSession(TradeSession session)
    {
        lock (_lock)
        {
            _sessionsByPlayer.Remove(session.Initiator.Id);
            _sessionsByPlayer.Remove(session.Partner.Id);
        }
        Log.Info($"Трейд удалён: {session.Initiator.Name} ↔ {session.Partner.Name}");
    }

    public static void CancelSession(TradeSession session, string reason)
    {
        RemoveSession(session);
        Log.Info($"Трейд отменён: {session.Initiator.Name} ↔ {session.Partner.Name} ({reason})");
    }
}

public class TradeSession
{
    public Guid Id { get; } = Guid.NewGuid();
    public Player Initiator { get; }
    public Player Partner { get; }

    public List<string> InitiatorItemIds { get; } = new();
    public List<string> PartnerItemIds { get; } = new();
    public int InitiatorGold { get; set; }
    public int PartnerGold { get; set; }

    public bool InitiatorConfirmed { get; set; }
    public bool PartnerConfirmed { get; set; }

    public bool InitiatorLocked { get; set; }
    public bool PartnerLocked { get; set; }

    public DateTime CreatedAt { get; } = DateTime.UtcNow;

    public TradeSession(Player initiator, Player partner)
    {
        Initiator = initiator;
        Partner = partner;
    }

    public Player? GetOther(Player player)
    {
        if (player.Id == Initiator.Id) return Partner;
        if (player.Id == Partner.Id) return Initiator;
        return null;
    }

    public bool IsParticipant(Player player)
        => player.Id == Initiator.Id || player.Id == Partner.Id;

    public void ResetConfirms()
    {
        InitiatorConfirmed = false;
        PartnerConfirmed = false;
    }

    public void LockPlayer(Player player)
    {
        if (player.Id == Initiator.Id) InitiatorLocked = true;
        else if (player.Id == Partner.Id) PartnerLocked = true;
    }

    public bool IsPlayerLocked(Player player)
    {
        if (player.Id == Initiator.Id) return InitiatorLocked;
        if (player.Id == Partner.Id) return PartnerLocked;
        return false;
    }

    public bool BothConfirmed => InitiatorConfirmed && PartnerConfirmed;
}

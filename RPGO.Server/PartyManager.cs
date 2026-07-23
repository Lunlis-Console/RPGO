using RPGGame.Shared.Models;
using RPGGame.Server.Network;

namespace RPGGame.Server;

public class PartyManager
{
    private readonly GameWorld _world;
    private INetworkHub? _hub;
    private readonly Dictionary<Guid, PartyData> _parties = new();
    private readonly object _lock = new();

    public PartyManager(GameWorld world)
    {
        _world = world;
    }

    public void SetHub(INetworkHub hub) => _hub = hub;

    public PartyData? CreateParty(Player leader, Player member)
    {
        lock (_lock)
        {
            if (leader.PartyId.HasValue || member.PartyId.HasValue)
                return null;

            var party = new PartyData
            {
                Id = Guid.NewGuid(),
                LeaderId = leader.Id,
                LeaderName = leader.Name
            };
            party.Members.Add(leader.Id);
            party.Members.Add(member.Id);

            _parties[party.Id] = party;
            leader.PartyId = party.Id;
            member.PartyId = party.Id;

            return party;
        }
    }

    public bool JoinParty(Player player, Guid partyId)
    {
        lock (_lock)
        {
            if (player.PartyId.HasValue)
                return false;
            if (!_parties.TryGetValue(partyId, out var party))
                return false;
            if (party.Members.Count >= 5)
                return false;

            party.Members.Add(player.Id);
            player.PartyId = partyId;
            return true;
        }
    }

    public void LeaveParty(Player player)
    {
        lock (_lock)
        {
            if (!player.PartyId.HasValue) return;
            if (!_parties.TryGetValue(player.PartyId.Value, out var party)) return;

            party.Members.Remove(player.Id);
            player.PartyId = null;

            if (party.LeaderId == player.Id && party.Members.Count > 0)
            {
                party.LeaderId = party.Members[0];
                party.LeaderName = _world.TryGetPlayer(party.Members[0], out var newLeader) && newLeader != null
                    ? newLeader.Name : party.LeaderName;
            }
        }
    }

    public void DisbandParty(Guid partyId)
    {
        lock (_lock)
        {
            if (!_parties.TryGetValue(partyId, out var party)) return;

            foreach (var memberId in party.Members)
            {
                if (_world.TryGetPlayer(memberId, out var member) && member != null)
                    member.PartyId = null;
            }
            _parties.Remove(partyId);
        }
    }

    public PartyData? GetParty(Guid partyId)
    {
        lock (_lock)
        {
            return _parties.TryGetValue(partyId, out var party) ? party : null;
        }
    }

    public PartyData? GetPartyForPlayer(Guid playerId)
    {
        lock (_lock)
        {
            foreach (var party in _parties.Values)
            {
                if (party.Members.Contains(playerId))
                    return party;
            }
            return null;
        }
    }

    public async Task SendPartyUpdateAsync(PartyData party)
    {
        if (_hub == null) return;
        var info = BuildPartyInfo(party);
        foreach (var memberId in party.Members)
        {
            if (!_world.TryGetPlayer(memberId, out var member) || member == null) continue;
            var conn = _world.GetConnectionByPlayerName(member.Name);
            if (conn != null)
            {
                await _hub.SendToClient(conn, new GameMessage
                {
                    Type = "party_update",
                    Data = info
                });
            }
        }
    }

    public async Task SendUpdateForAsync(Player player)
    {
        if (!player.PartyId.HasValue) return;
        var party = GetParty(player.PartyId.Value);
        if (party != null) await SendPartyUpdateAsync(party);
    }

    public async Task DisbandAndNotifyAsync(Guid partyId)
    {
        lock (_lock)
        {
            if (!_parties.TryGetValue(partyId, out var party)) return;

            foreach (var memberId in party.Members)
            {
                if (_world.TryGetPlayer(memberId, out var member) && member != null)
                    member.PartyId = null;
            }
            _parties.Remove(partyId);

            _ = Task.Run(async () =>
            {
                if (_hub == null) return;
                foreach (var memberId in party.Members)
                {
                    if (!_world.TryGetPlayer(memberId, out var member) || member == null) continue;
                    var conn = _world.GetConnectionByPlayerName(member.Name);
                    if (conn != null)
                    {
                        await _hub.SendToClient(conn, new GameMessage
                        {
                            Type = "party_disbanded",
                            Data = (object?)null
                        });
                    }
                }
            });
        }
    }

    private PartyInfo BuildPartyInfo(PartyData party)
    {
        var info = new PartyInfo
        {
            PartyId = party.Id,
            LeaderId = party.LeaderId,
            LeaderName = party.LeaderName,
            Members = new List<PartyMemberInfo>()
        };

        foreach (var memberId in party.Members)
        {
            if (!_world.TryGetPlayer(memberId, out var member) || member == null) continue;
            int maxHp = member.MaxHealth + member.Equipment.GetBonusMaxHealth();
            int health = Math.Min(member.Health, maxHp);
            info.Members.Add(new PartyMemberInfo
            {
                PlayerId = member.Id,
                Name = member.Name,
                Health = health,
                MaxHealth = maxHp,
                Level = member.Level
            });
        }
        return info;
    }
}

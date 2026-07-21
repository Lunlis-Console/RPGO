using RPGGame.Shared.Models;
using System.Collections.Concurrent;

namespace RPGGame.Server;

public class PartyManager
{
    private static readonly Dictionary<Guid, PartyData> _parties = new();
    private static readonly object _lock = new();

    public static PartyData? CreateParty(Player leader, Player member)
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

    public static bool JoinParty(Player player, Guid partyId)
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

    public static void LeaveParty(Player player)
    {
        lock (_lock)
        {
            if (!player.PartyId.HasValue) return;
            if (!_parties.TryGetValue(player.PartyId.Value, out var party)) return;

            party.Members.Remove(player.Id);
            player.PartyId = null;

            // Если вышел лидер и в группе ещё кто-то остался — назначаем
            // нового лидера (первого по списку), иначе группа «застрянет»
            // с LeaderId удалённого игрока и никто не сможет приглашать.
            if (party.LeaderId == player.Id && party.Members.Count > 0)
            {
                party.LeaderId = party.Members[0];
                party.LeaderName = Program.World.TryGetPlayer(party.Members[0], out var newLeader) && newLeader != null
                    ? newLeader.Name : party.LeaderName;
            }
        }
    }

    public static void DisbandParty(Guid partyId)
    {
        lock (_lock)
        {
            if (!_parties.TryGetValue(partyId, out var party)) return;

            foreach (var memberId in party.Members)
            {
                if (Program.World.TryGetPlayer(memberId, out var member) && member != null)
                    member.PartyId = null;
            }
            _parties.Remove(partyId);
        }
    }

    public static PartyData? GetParty(Guid partyId)
    {
        lock (_lock)
        {
            return _parties.TryGetValue(partyId, out var party) ? party : null;
        }
    }

    public static PartyData? GetPartyForPlayer(Guid playerId)
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

    public static async Task SendPartyUpdateAsync(PartyData party)
    {
        var info = BuildPartyInfo(party);
        foreach (var memberId in party.Members)
        {
            if (!Program.World.TryGetPlayer(memberId, out var member) || member == null) continue;
            var conn = Program.World.GetConnectionByPlayerName(member.Name);
            if (conn != null)
            {
                await Program.Hub.SendToClient(conn, new GameMessage
                {
                    Type = "party_update",
                    Data = info
                });
            }
        }
    }

    public static async Task DisbandAndNotifyAsync(Guid partyId)
    {
        lock (_lock)
        {
            if (!_parties.TryGetValue(partyId, out var party)) return;

            foreach (var memberId in party.Members)
            {
                if (Program.World.TryGetPlayer(memberId, out var member) && member != null)
                    member.PartyId = null;
            }
            _parties.Remove(partyId);

            _ = Task.Run(async () =>
            {
                foreach (var memberId in party.Members)
                {
                    if (!Program.World.TryGetPlayer(memberId, out var member) || member == null) continue;
                    var conn = Program.World.GetConnectionByPlayerName(member.Name);
                    if (conn != null)
                    {
                        await Program.Hub.SendToClient(conn, new GameMessage
                        {
                            Type = "party_disbanded",
                            Data = (object?)null
                        });
                    }
                }
            });
        }
    }

    private static PartyInfo BuildPartyInfo(PartyData party)
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
            if (!Program.World.TryGetPlayer(memberId, out var member) || member == null) continue;
            // MaxHealth с учётом бонуса экипировки (как в status/inventory),
            // иначе в окне группы ХП показывается заниженным после надевания брони.
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

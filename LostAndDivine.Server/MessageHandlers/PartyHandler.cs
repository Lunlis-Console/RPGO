using LostAndDivine.Server.Network;
using System.Text.Json;
using LostAndDivine.Server.Services;
using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;

namespace LostAndDivine.Server.MessageHandlers;

public class PartyHandler : BaseHandler
{
    public PartyHandler(GameServices svc) : base(svc) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;

        string action = message.Type;
        JsonElement el = default;
        if (message.Data is JsonElement je && je.ValueKind != JsonValueKind.Undefined)
            el = je;

        if (action == "party_invite")
        {
            string targetName = el.TryGetProperty("TargetName", out var tn) ? tn.GetString() ?? "" : "";
            if (string.IsNullOrEmpty(targetName))
            {
                await SendError(connection, ErrorCodes.InvalidRequest, "������� ��� ������");
                return;
            }

            if (player.PartyId.HasValue)
            {
                var myParty = Svc.Party.GetParty(player.PartyId.Value);
                if (myParty == null || myParty.LeaderId != player.Id)
                {
                    await SendError(connection, ErrorCodes.InvalidRequest, "� ������ ����� ���������� ������ �����");
                    return;
                }
                if (myParty.Members.Count >= 5)
                {
                    await SendError(connection, ErrorCodes.InvalidRequest, "������ ������ (����. 5)");
                    return;
                }
            }

            if (!World.TryGetPlayerByName(targetName, out var target) || target == null)
            {
                await SendError(connection, ErrorCodes.InvalidRequest, $"����� {targetName} �� ������");
                return;
            }

            if (target.PartyId.HasValue)
            {
                await SendError(connection, ErrorCodes.InvalidRequest, $"{targetName} ��� � ������");
                return;
            }

            if (Svc.Trade.IsInTrade(target))
            {
                await SendError(connection, ErrorCodes.InvalidRequest, $"{targetName} ������ ����� �������");
                return;
            }

            if (target.Id == player.Id)
            {
                await SendError(connection, ErrorCodes.InvalidRequest, "������ ���������� ����");
                return;
            }

            var targetConn = World.FindClientByPlayer(target);
            if (targetConn != null)
            {
                await SendToClient(targetConn, new GameMessage
                {
                    Type = "party_invite_received",
                    Data = new { InviterName = player.Name, InviterId = player.Id }
                });
            }

            await SendToClient(connection, new GameMessage
            {
                Type = "party_invite_sent",
                Data = new { TargetName = target.Name }
            });
        }
        else if (action == "party_accept")
        {
            string inviterName = el.TryGetProperty("InviterName", out var inv) ? inv.GetString() ?? "" : "";
            if (string.IsNullOrEmpty(inviterName)) return;

            if (!World.TryGetPlayerByName(inviterName, out var inviter) || inviter == null)
            {
                await SendError(connection, ErrorCodes.InvalidRequest, "������������ ����� �� ������");
                return;
            }

            if (inviter.PartyId.HasValue)
            {
                var existingParty = Svc.Party.GetParty(inviter.PartyId.Value);
                if (existingParty != null)
                {
                    if (existingParty.Members.Count >= 5)
                    {
                        await SendError(connection, ErrorCodes.InvalidRequest, "������ ������ (����. 5)");
                        return;
                    }

                    if (Svc.Party.JoinParty(player, existingParty.Id))
                    {
                        await Svc.Party.SendPartyUpdateAsync(existingParty);
                    }
                    return;
                }
            }

            var party = Svc.Party.CreateParty(inviter, player);
            if (party != null)
            {
                Log.Info($"������ �������: {inviter.Name} + {player.Name}");
                await Svc.Party.SendPartyUpdateAsync(party);
            }
        }
        else if (action == "party_decline")
        {
            string inviterName = el.TryGetProperty("InviterName", out var inv) ? inv.GetString() ?? "" : "";
            if (string.IsNullOrEmpty(inviterName)) return;

            if (!World.TryGetPlayerByName(inviterName, out var inviter) || inviter == null) return;

            var inviterConn = World.FindClientByPlayer(inviter);
            if (inviterConn != null)
            {
                await SendToClient(inviterConn, new GameMessage
                {
                    Type = "party_invite_declined",
                    Data = new { TargetName = player.Name }
                });
            }
        }
        else if (action == "party_transfer")
        {
            string targetName = el.TryGetProperty("TargetName", out var tn) ? tn.GetString() ?? "" : "";
            if (string.IsNullOrEmpty(targetName))
            {
                await SendError(connection, ErrorCodes.InvalidRequest, "������� ��� ������");
                return;
            }

            if (!player.PartyId.HasValue)
            {
                await SendError(connection, ErrorCodes.InvalidRequest, "�� �� � ������");
                return;
            }

            var party = Svc.Party.GetParty(player.PartyId.Value);
            if (party == null)
            {
                await SendError(connection, ErrorCodes.InvalidRequest, "������ �� �������");
                return;
            }

            if (party.LeaderId != player.Id)
            {
                await SendError(connection, ErrorCodes.InvalidRequest, "�������� ��������� ����� ������ �����");
                return;
            }

            if (!World.TryGetPlayerByName(targetName, out var target) || target == null)
            {
                await SendError(connection, ErrorCodes.InvalidRequest, $"����� {targetName} �� ������");
                return;
            }

            if (target.Id == player.Id)
            {
                await SendError(connection, ErrorCodes.InvalidRequest, "������ �������� ��������� ����");
                return;
            }

            if (!party.Members.Contains(target.Id))
            {
                await SendError(connection, ErrorCodes.InvalidRequest, $"{targetName} �� ������� � ����� ������");
                return;
            }

            party.LeaderId = target.Id;
            party.LeaderName = target.Name;
            Log.Info($"{player.Name} ������� ��������� {target.Name}");
            await Svc.Party.SendPartyUpdateAsync(party);

            var targetConn = World.FindClientByPlayer(target);
            if (targetConn != null)
                await SendToClient(targetConn, GameMessage.SystemChat("�� ������ ����� ������."));
        }
        else if (action == "party_kick")
        {
            string targetName = el.TryGetProperty("TargetName", out var tn) ? tn.GetString() ?? "" : "";
            if (string.IsNullOrEmpty(targetName))
            {
                await SendError(connection, ErrorCodes.InvalidRequest, "������� ��� ������");
                return;
            }

            if (!player.PartyId.HasValue)
            {
                await SendError(connection, ErrorCodes.InvalidRequest, "�� �� � ������");
                return;
            }

            var party = Svc.Party.GetParty(player.PartyId.Value);
            if (party == null)
            {
                await SendError(connection, ErrorCodes.InvalidRequest, "������ �� �������");
                return;
            }

            if (party.LeaderId != player.Id)
            {
                await SendError(connection, ErrorCodes.InvalidRequest, "��������� ����� ������ �����");
                return;
            }

            if (!World.TryGetPlayerByName(targetName, out var target) || target == null)
            {
                await SendError(connection, ErrorCodes.InvalidRequest, $"����� {targetName} �� ������");
                return;
            }

            if (target.Id == player.Id)
            {
                await SendError(connection, ErrorCodes.InvalidRequest, "������ ��������� ����");
                return;
            }

            if (!party.Members.Contains(target.Id))
            {
                await SendError(connection, ErrorCodes.InvalidRequest, $"{targetName} �� ������� � ����� ������");
                return;
            }

            Log.Info($"{player.Name} �������� {target.Name} �� ������");

            // ����������� �������� ������
            Svc.Party.LeaveParty(target);

            // ���������� ������������
            var targetConn = World.FindClientByPlayer(target);
            if (targetConn != null)
            {
                await SendToClient(targetConn, new GameMessage
                {
                    Type = "party_disbanded",
                    Data = (object?)null
                });
                await SendToClient(targetConn, GameMessage.SystemChat($"�� ��������� �� ������ ({party.LeaderName})."));
            }

            if (party.Members.Count >= 2)
            {
                await Svc.Party.SendPartyUpdateAsync(party);
            }
            else
            {
                await Svc.Party.DisbandAndNotifyAsync(party.Id);
            }
        }
        else if (action == "party_leave")
        {
            if (!player.PartyId.HasValue)
            {
                await SendError(connection, ErrorCodes.InvalidRequest, "�� �� � ������");
                return;
            }

            var party = Svc.Party.GetParty(player.PartyId.Value);
            Svc.Party.LeaveParty(player);

            if (party != null)
            {
                Log.Info($"{player.Name} ������� ����");

                if (party.Members.Count >= 2)
                {
                    // ������ ����: ��� ���������� ������ (� �������� ����� �������)
                    await Svc.Party.SendPartyUpdateAsync(party);
                }
                else
                {
                    // ������ ��������� (������� 1 ��� 0): DisbandAndNotifyAsync
                    // ������� party_disbanded ����������. ������ �������� (��� �� �
                    // Members) ��� ��������, ����� � ���� � HUD ����� ������ ������.
                    await DisbandNotifySelf(connection);
                    await Svc.Party.DisbandAndNotifyAsync(party.Id);
                }
            }
        }
    }

    private async Task DisbandNotifySelf(ClientConnection connection)
    {
        await SendToClient(connection, new GameMessage
        {
            Type = "party_disbanded",
            Data = (object?)null
        });
    }
}

using RPGGame.Server.Network;
using System.Text.Json;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;

namespace RPGGame.Server.MessageHandlers;

public class PartyHandler : BaseHandler
{
    public PartyHandler(GameWorld world, INetworkHub hub) : base(world, hub) { }

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
                await SendError(connection, ErrorCodes.InvalidRequest, "Укажите имя игрока");
                return;
            }

            if (player.PartyId.HasValue)
            {
                var myParty = PartyManager.GetParty(player.PartyId.Value);
                if (myParty == null || myParty.LeaderId != player.Id)
                {
                    await SendError(connection, ErrorCodes.InvalidRequest, "В группу может приглашать только лидер");
                    return;
                }
                if (myParty.Members.Count >= 5)
                {
                    await SendError(connection, ErrorCodes.InvalidRequest, "Группа полная (макс. 5)");
                    return;
                }
            }

            if (!World.TryGetPlayerByName(targetName, out var target) || target == null)
            {
                await SendError(connection, ErrorCodes.InvalidRequest, $"Игрок {targetName} не найден");
                return;
            }

            if (target.PartyId.HasValue)
            {
                await SendError(connection, ErrorCodes.InvalidRequest, $"{targetName} уже в группе");
                return;
            }

            if (TradeManager.IsInTrade(target))
            {
                await SendError(connection, ErrorCodes.InvalidRequest, $"{targetName} сейчас занят обменом");
                return;
            }

            if (target.Id == player.Id)
            {
                await SendError(connection, ErrorCodes.InvalidRequest, "Нельзя пригласить себя");
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
                await SendError(connection, ErrorCodes.InvalidRequest, "Пригласивший игрок не найден");
                return;
            }

            if (inviter.PartyId.HasValue)
            {
                var existingParty = PartyManager.GetParty(inviter.PartyId.Value);
                if (existingParty != null)
                {
                    if (existingParty.Members.Count >= 5)
                    {
                        await SendError(connection, ErrorCodes.InvalidRequest, "Группа полная (макс. 5)");
                        return;
                    }

                    if (PartyManager.JoinParty(player, existingParty.Id))
                    {
                        await PartyManager.SendPartyUpdateAsync(existingParty);
                    }
                    return;
                }
            }

            var party = PartyManager.CreateParty(inviter, player);
            if (party != null)
            {
                Log.Info($"Группа создана: {inviter.Name} + {player.Name}");
                await PartyManager.SendPartyUpdateAsync(party);
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
        else if (action == "party_leave")
        {
            if (!player.PartyId.HasValue)
            {
                await SendError(connection, ErrorCodes.InvalidRequest, "Вы не в группе");
                return;
            }

            var party = PartyManager.GetParty(player.PartyId.Value);
            PartyManager.LeaveParty(player);

            if (party != null)
            {
                Log.Info($"{player.Name} покинул пати");

                // Уведомляем ушедшего игрока
                await SendToClient(connection, new GameMessage
                {
                    Type = "party_disbanded",
                    Data = (object?)null
                });

                if (party.Members.Count >= 2)
                {
                    await PartyManager.SendPartyUpdateAsync(party);
                }
                else
                {
                    await PartyManager.DisbandAndNotifyAsync(party.Id);
                }
            }
        }
    }
}

using LostAndDivine.Server.Network;
using LostAndDivine.Server.Services;
using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;
using System.Text.Json;

namespace LostAndDivine.Server.MessageHandlers;

public class ResetSkillsHandler : BaseHandler
{
    public ResetSkillsHandler(GameServices svc) : base(svc) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;

        int refunded = player.LearnedSkills.Count;
        if (refunded == 0)
        {
            await SendError(connection, ErrorCodes.InvalidRequest, "��� ��������� ������� ��� ������!");
            return;
        }

        player.LearnedSkills.Clear();
        player.SkillRanks.Clear();
        player.SkillPoints = player.Level / 2;

        Log.Info($"{player.Name} ������� ������. ���������� {player.SkillPoints} �����.");
        Svc.Persistence.EnqueueSave(player);

        await SendToClient(connection, new GameMessage
        {
            Type = "chat",
            Data = new { Name = "�������", Text = $"������ ��������! ��� ���� ������� ���������� ({player.SkillPoints})." }
        });
        await Hub.SendSkills(connection);
    }
}

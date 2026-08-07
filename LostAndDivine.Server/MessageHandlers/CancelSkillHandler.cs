using LostAndDivine.Server.Network;
using LostAndDivine.Server.Services;
using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;

namespace LostAndDivine.Server.MessageHandlers;

/// <summary>
/// ������ ������� ������������� ������� (������� / ������� � ���).
/// ������ ��� �� ��� �� ����� ������, ������� ��� ����������.
/// </summary>
public class CancelSkillHandler : BaseHandler
{
    public CancelSkillHandler(GameServices svc) : base(svc) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;

        player.QueuedSkillIds.Clear();
        await UseSkillHandler.SendSkillQueue(connection, player, Hub);
        await SendToClient(connection, new GameMessage
        {
            Type = "chat",
            Data = new { Name = "���", Text = "������� ������� �������." }
        });
    }
}

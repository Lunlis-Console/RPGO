using LostAndDivine.Server.Network;
using LostAndDivine.Server.Services;
using LostAndDivine.Shared.Models;

namespace LostAndDivine.Server.MessageHandlers;

/// <summary>
/// Ручная атака отключена — используется автоатака через select_target.
/// </summary>
public class AttackHandler : BaseHandler
{
    public AttackHandler(GameServices svc) : base(svc) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;
        await SendToClient(connection, new GameMessage
        {
            Type = "chat",
            Data = new { Name = "Система", Text = "Нажмите ЛКМ на монстра, чтобы выбрать цель для атаки." }
        });
    }
}

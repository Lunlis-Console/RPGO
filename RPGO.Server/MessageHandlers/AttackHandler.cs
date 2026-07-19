using RPGGame.Server.Network;
using RPGGame.Shared.Models;

namespace RPGGame.Server.MessageHandlers;

/// <summary>
/// Ручная атака отключена — используется автоатака через select_target.
/// </summary>
public class AttackHandler : BaseHandler
{
    public AttackHandler(GameWorld world, INetworkHub hub) : base(world, hub) { }

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

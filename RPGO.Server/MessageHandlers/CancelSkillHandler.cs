using RPGGame.Server.Network;
using RPGGame.Server.Services;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;

namespace RPGGame.Server.MessageHandlers;

/// <summary>
/// Отмена очереди заготовленных навыков (прекаст / очередь в бою).
/// Клиент шлёт по ЛКМ по слоту навыка, который уже заготовлен.
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
            Data = new { Name = "Бой", Text = "Очередь навыков очищена." }
        });
    }
}

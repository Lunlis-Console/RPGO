using LostAndDivine.Server.Network;
using LostAndDivine.Server.Services;
using LostAndDivine.Shared.Models;

namespace LostAndDivine.Server.MessageHandlers;

/// <summary>
/// Клиент запрашивает тайлы текущей зоны (self-heal).
/// Срабатывает, когда первый map_update с тайлами был потерян из-за гонки
/// «map_update до создания GameScreen» — клиент просит тайлы повторно,
/// сервер отвечает тем же payload'ом, что и zone_transition.
/// </summary>
public class TileRequestHandler : BaseHandler
{
    public TileRequestHandler(GameServices svc) : base(svc) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;

        if (player.CurrentZoneId == Balance.MainZoneId)
        {
            // Открытый мир: тайлы целиком не передаются (карта 3000x1700 не влезает
            // в лимит сообщения) — отвечаем блоком секторов вокруг игрока.
            await Hub.SendSectorsAround(connection, player);
            return;
        }

        await Hub.SendZoneTransition(connection, player);
    }
}

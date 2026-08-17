using LostAndDivine.Server.Network;
using LostAndDivine.Server.Services;
using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;
using System.Text.Json;

namespace LostAndDivine.Server.MessageHandlers;

/// <summary>
/// Клиент запрашивает сектор открытого мира (main) по координатам сетки.
/// Сервер отвечает sector_data; сектор передаётся один раз за соединение.
/// </summary>
public class SectorRequestHandler : BaseHandler
{
    public SectorRequestHandler(GameServices svc) : base(svc) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;

        var request = JsonSerializer.Deserialize<SectorRequest>(JsonSerializer.Serialize(message.Data));
        if (request == null) return;
        if (request.Col < 0 || request.Col >= Balance.SectorCols) return;
        if (request.Row < 0 || request.Row >= Balance.SectorRows) return;

        await Hub.SendSectorData(connection, request.Col, request.Row);
    }
}

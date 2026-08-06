using LostAndDivine.Server.Services;
using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;

namespace LostAndDivine.Server.MessageHandlers;

/// <summary>
/// Обработчик одного типа входящего сообщения от клиента.
/// Хендлеры получают GameWorld через конструктор (для тестируемости),
/// а данные сообщения (message.Data) десериализуют локально, внутри Handle.
/// </summary>
public interface IMessageHandler
{
    Task Handle(ClientConnection connection, GameMessage message, Player? player);
}

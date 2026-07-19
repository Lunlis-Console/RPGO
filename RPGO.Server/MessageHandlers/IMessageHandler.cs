using RPGGame.Shared.Models;
using RPGGame.Shared.Network;

namespace RPGGame.Server.MessageHandlers;

/// <summary>
/// Обработчик одного типа входящего сообщения от клиента.
/// Хендлеры получают GameWorld через конструктор (для тестируемости),
/// а данные сообщения (message.Data) десериализуют локально, внутри Handle.
/// </summary>
public interface IMessageHandler
{
    Task Handle(ClientConnection connection, GameMessage message, Player? player);
}

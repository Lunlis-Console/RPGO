using LostAndDivine.Server.Services;
using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;

namespace LostAndDivine.Server.MessageHandlers;

/// <summary>
/// ���������� ������ ���� ��������� ��������� �� �������.
/// �������� �������� GameWorld ����� ����������� (��� �������������),
/// � ������ ��������� (message.Data) ������������� ��������, ������ Handle.
/// </summary>
public interface IMessageHandler
{
    Task Handle(ClientConnection connection, GameMessage message, Player? player);
}

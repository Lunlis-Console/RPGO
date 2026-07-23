using RPGGame.Shared.Models;
using RPGGame.Shared.Network;

namespace RPGGame.Server.Network;

/// <summary>
/// Контракт сетевого слоя сервера: отправка и рассылка сообщений клиентам.
/// Позволяет обработчикам и менеджерам не зависеть от конкретной реализации
/// (и от статического Program), что упрощает тестирование.
/// </summary>
public interface INetworkHub
{
    Task BroadcastMapAsync();
    Task BroadcastChatAsync(string playerName, string text);
    Task BroadcastChatAsync(ChatChannel channel, string from, string text);
    Task SendChatToAsync(ClientConnection connection, ChatChannel channel, string from, string text, string? to = null);
    Task SendToClient(ClientConnection connection, GameMessage message);
    Task SendToAllAsync(GameMessage message);
    Task SendStatusAsync(ClientConnection connection, Player player);
    Task SendInventoryAndStatus(ClientConnection connection, Player player);
    Task SendDamageNearbyAsync(int x, int y, GameMessage damageMsg, Player? exclude);
    Task SendQuestLog(ClientConnection connection, Player player);
    Task SendHotbar(ClientConnection connection, Player player);
    Task SendSkills(ClientConnection connection);
    Task SendError(ClientConnection connection, string code, string message);
    Task SendFriendListToAsync(ClientConnection connection, Player player);
    StatsBreakdown BuildBreakdown(Player player);
    Task KickPlayer(ClientConnection connection, string reason);
}

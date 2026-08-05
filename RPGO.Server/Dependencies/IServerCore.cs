using RPGGame.Server.Network;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;

namespace RPGGame.Server.Dependencies;

public interface IServerCore
{
    GameWorld World { get; }
    INetworkHub Hub { get; }
    ZoneManager Zones { get; }
    PathfindingService Pathfinding { get; }
    Task ChatTo(ClientConnection? conn, ChatChannel channel, string name, string text);
    Task ChatToC(ClientConnection? conn, string name, string text);
    Task ReloadContent(ClientConnection? connection = null);
}

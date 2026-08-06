using LostAndDivine.Server.Network;
using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;

namespace LostAndDivine.Server.Dependencies;

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

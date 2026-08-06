using LostAndDivine.Server.Dependencies;
using LostAndDivine.Server.Network;
using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;

namespace LostAndDivine.Server;

public interface IGameServices : IServerCore, ICombatInfra, IGameWorldDeps
{
}

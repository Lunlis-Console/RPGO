using RPGGame.Server.Dependencies;
using RPGGame.Server.Network;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;

namespace RPGGame.Server;

public interface IGameServices : IServerCore, ICombatInfra, IGameWorldDeps
{
}

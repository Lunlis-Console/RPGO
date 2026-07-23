using RPGGame.Shared.Models;
using RPGGame.Shared.Utils;

namespace RPGGame.Server;

public class PathfindingService
{
    private readonly GameWorld _world;
    private readonly MerchantManager _merchant;
    private readonly QuestManager _quests;

    public PathfindingService(GameWorld world, MerchantManager merchant, QuestManager quests)
    {
        _world = world;
        _merchant = merchant;
        _quests = quests;
    }

    public List<(int X, int Y)> FindPath(int startX, int startY, int targetX, int targetY)
    {
        return Shared.Utils.Pathfinding.FindPath(startX, startY, targetX, targetY,
            _world.Map.Width, _world.Map.Height,
            (nx, ny) =>
                _world.Map.IsObstacle(nx, ny) ||
                (nx == _merchant.MerchantX && ny == _merchant.MerchantY) ||
                (nx == _quests.BoardX && ny == _quests.BoardY));
    }
}

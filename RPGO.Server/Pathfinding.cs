using RPGGame.Shared.Models;
using RPGGame.Shared.Utils;

namespace RPGGame.Server;

public static class Pathfinding
{
    private static GameMap Map => Program.World.Map;

    public static List<(int X, int Y)> FindPath(int startX, int startY, int targetX, int targetY)
    {
        return Shared.Utils.Pathfinding.FindPath(startX, startY, targetX, targetY,
            Map.Width, Map.Height,
            (nx, ny) =>
                Map.IsObstacle(nx, ny) ||
                (nx == MerchantManager.MerchantX && ny == MerchantManager.MerchantY) ||
                (nx == QuestManager.BoardX && ny == QuestManager.BoardY));
    }
}

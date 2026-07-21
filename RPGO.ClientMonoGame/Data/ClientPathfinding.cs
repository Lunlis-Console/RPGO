using RPGGame.Shared.Utils;

namespace RPGGame.ClientMonoGame.Data;

public static class ClientPathfinding
{
    public static List<(int X, int Y)> FindPath(int sx, int sy, int tx, int ty,
        int merchantX = -1, int merchantY = -1, int boardX = -1, int boardY = -1,
        int worldW = 100, int worldH = 100)
    {
        return Shared.Utils.Pathfinding.FindPath(sx, sy, tx, ty, worldW, worldH,
            (nx, ny) =>
                (nx == merchantX && ny == merchantY) ||
                (nx == boardX && ny == boardY));
    }
}

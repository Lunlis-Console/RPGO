using RPGGame.Shared.Models;

namespace RPGGame.Server;

public static class Pathfinding
{
    private static GameMap Map => Program.World.Map;

    private static readonly int[] Dx = { 0, 0, -1, 1, -1, -1, 1, 1 };
    private static readonly int[] Dy = { -1, 1, 0, 0, -1, 1, -1, 1 };

    public static List<(int X, int Y)> FindPath(int startX, int startY, int targetX, int targetY)
    {
        if (startX == targetX && startY == targetY)
            return new List<(int, int)>();

        if (targetX < 0 || targetX >= Map.Width || targetY < 0 || targetY >= Map.Height)
            return new List<(int, int)>();

        var visited = new bool[Map.Width, Map.Height];
        var parent = new (int X, int Y)[Map.Width, Map.Height];
        var queue = new Queue<(int X, int Y)>();

        visited[startX, startY] = true;
        queue.Enqueue((startX, startY));

        bool found = false;
        while (queue.Count > 0)
        {
            var (cx, cy) = queue.Dequeue();

            if (cx == targetX && cy == targetY)
            {
                found = true;
                break;
            }

            for (int i = 0; i < 8; i++)
            {
                int nx = cx + Dx[i];
                int ny = cy + Dy[i];

                if (nx < 0 || nx >= Map.Width || ny < 0 || ny >= Map.Height) continue;
                if (visited[nx, ny]) continue;
                if (nx == MerchantManager.MerchantX && ny == MerchantManager.MerchantY) continue;
                if (nx == QuestManager.BoardX && ny == QuestManager.BoardY) continue;

                // Диагональные движения: нельзя срезать углы через блокированные клетки
                if (i >= 4) // диагональ
                {
                    int ox1 = cx + Dx[i];
                    int oy1 = cy;
                    int ox2 = cx;
                    int oy2 = cy + Dy[i];
                    if ((ox1 == MerchantManager.MerchantX && oy1 == MerchantManager.MerchantY) ||
                        (ox1 == QuestManager.BoardX && oy1 == QuestManager.BoardY) ||
                        (ox2 == MerchantManager.MerchantX && oy2 == MerchantManager.MerchantY) ||
                        (ox2 == QuestManager.BoardX && oy2 == QuestManager.BoardY))
                    {
                        continue; // срезаем угол через препятствие
                    }
                }

                visited[nx, ny] = true;
                parent[nx, ny] = (cx, cy);
                queue.Enqueue((nx, ny));
            }
        }

        if (!found)
            return new List<(int, int)>();

        var path = new List<(int X, int Y)>();
        int x = targetX, y = targetY;
        while (x != startX || y != startY)
        {
            path.Add((x, y));
            var p = parent[x, y];
            x = p.X;
            y = p.Y;
        }
        path.Reverse();
        return path;
    }
}

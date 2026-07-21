namespace RPGGame.Shared.Utils;

public static class Pathfinding
{
    private static readonly int[] Dx = { 0, 0, -1, 1 };
    private static readonly int[] Dy = { -1, 1, 0, 0 };

    public static List<(int X, int Y)> FindPath(
        int startX, int startY, int targetX, int targetY,
        int worldW, int worldH,
        Func<int, int, bool> isBlocked)
    {
        if (startX == targetX && startY == targetY)
            return new List<(int, int)>();

        if (targetX < 0 || targetX >= worldW || targetY < 0 || targetY >= worldH)
            return new List<(int, int)>();

        var visited = new bool[worldW, worldH];
        var parent = new (int X, int Y)[worldW, worldH];
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

            for (int i = 0; i < Dx.Length; i++)
            {
                int nx = cx + Dx[i];
                int ny = cy + Dy[i];

                if (nx < 0 || nx >= worldW || ny < 0 || ny >= worldH) continue;
                if (visited[nx, ny]) continue;
                if (isBlocked(nx, ny)) continue;

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

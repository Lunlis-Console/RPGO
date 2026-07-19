namespace RPGGame.ClientMonoGame.Data;

/// <summary>
/// Клиентский BFS для отрисовки пути перемещения на карте.
/// </summary>
public static class ClientPathfinding
{
    public static List<(int X, int Y)> FindPath(int sx, int sy, int tx, int ty,
        int merchantX = -1, int merchantY = -1, int boardX = -1, int boardY = -1,
        int worldW = 100, int worldH = 100)
    {
        if (sx == tx && sy == ty) return new();
        if (tx < 0 || tx >= worldW || ty < 0 || ty >= worldH) return new();

        var visited = new bool[worldW, worldH];
        var parent = new (int X, int Y)[worldW, worldH];
        var queue = new Queue<(int X, int Y)>();
        visited[sx, sy] = true;
        queue.Enqueue((sx, sy));
        int[] dx = { 0, 0, -1, 1, -1, -1, 1, 1 };
        int[] dy = { -1, 1, 0, 0, -1, 1, -1, 1 };
        bool found = false;
        while (queue.Count > 0)
        {
            var (cx, cy) = queue.Dequeue();
            if (cx == tx && cy == ty) { found = true; break; }
            for (int i = 0; i < 8; i++)
            {
                int nx = cx + dx[i], ny = cy + dy[i];
                if (nx < 0 || nx >= worldW || ny < 0 || ny >= worldH) continue;
                if (visited[nx, ny]) continue;
                if (nx == merchantX && ny == merchantY) continue;
                if (nx == boardX && ny == boardY) continue;

                if (i >= 4)
                {
                    int ox1 = cx + dx[i], oy1 = cy;
                    int ox2 = cx, oy2 = cy + dy[i];
                    if ((ox1 == merchantX && oy1 == merchantY) ||
                        (ox1 == boardX && oy1 == boardY) ||
                        (ox2 == merchantX && oy2 == merchantY) ||
                        (ox2 == boardX && oy2 == boardY))
                        continue;
                }

                visited[nx, ny] = true;
                parent[nx, ny] = (cx, cy);
                queue.Enqueue((nx, ny));
            }
        }
        if (!found) return new();
        var path = new List<(int, int)>();
        int x2 = tx, y2 = ty;
        while (x2 != sx || y2 != sy) { path.Add((x2, y2)); var p = parent[x2, y2]; x2 = p.X; y2 = p.Y; }
        path.Reverse();
        return path;
    }
}

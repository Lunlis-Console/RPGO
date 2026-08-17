namespace LostAndDivine.Shared.Utils;

public static class Pathfinding
{
    private static readonly int[] Dx = { 0, 0, -1, 1 };
    private static readonly int[] Dy = { -1, 1, 0, 0 };

    /// <summary>
    /// Максимум посещённых клеток за один поиск. Защита от бесконечного перебора
    /// на больших картах (секторный мир 3000x1700): после лимита поиск прекращается.
    /// </summary>
    private const int MaxVisited = 200_000;

    public static List<(int X, int Y)> FindPath(
        int startX, int startY, int targetX, int targetY,
        int worldW, int worldH,
        Func<int, int, bool> isBlocked)
    {
        if (startX < 0 || startX >= worldW || startY < 0 || startY >= worldH)
            return new List<(int, int)>();

        if (startX == targetX && startY == targetY)
            return new List<(int, int)>();

        if (targetX < 0 || targetX >= worldW || targetY < 0 || targetY >= worldH)
            return new List<(int, int)>();

        // Разреженный BFS: посещаем только реально пройденные клетки, а не всю карту.
        // На секторном мире (5.1 млн клеток) плотные bool[worldW, worldH]-массивы
        // непозволительны; здесь память пропорциональна числу исследованных клеток.
        var visited = new HashSet<(int X, int Y)>();
        var parent = new Dictionary<(int X, int Y), (int X, int Y)>();
        var queue = new Queue<(int X, int Y)>();

        visited.Add((startX, startY));
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
                if (isBlocked(nx, ny)) continue;
                if (!visited.Add((nx, ny))) continue;

                parent[(nx, ny)] = (cx, cy);
                queue.Enqueue((nx, ny));

                if (visited.Count >= MaxVisited)
                {
                    // Слишком много клеток — считаем путь ненайденным
                    if (nx == targetX && ny == targetY) found = true;
                    goto done;
                }
            }
        }

        done:
        if (!found)
            return new List<(int, int)>();

        // Восстанавливаем путь
        var raw = new List<(int X, int Y)>();
        int x = targetX, y = targetY;
        while (x != startX || y != startY)
        {
            raw.Add((x, y));
            var p = parent[(x, y)];
            x = p.X;
            y = p.Y;
        }
        raw.Reverse();

        // Сглаживание: переставляем шаги так, чтобы направление чередовалось
        // (вместо "10 вверх → 10 влево" делаем "вверх, влево, вверх, влево...")
        return SmoothPath(startX, startY, targetX, targetY, raw, isBlocked);
    }

    private static List<(int X, int Y)> SmoothPath(
        int startX, int startY, int targetX, int targetY,
        List<(int X, int Y)> raw,
        Func<int, int, bool> isBlocked)
    {
        int dx = targetX - startX;
        int dy = targetY - startY;
        int stepX = dx == 0 ? 0 : dx > 0 ? 1 : -1;
        int stepY = dy == 0 ? 0 : dy > 0 ? 1 : -1;

        // Если цель на одной оси — никакого L-пути нет, возвращаем как есть
        if (dx == 0 || dy == 0)
            return raw;

        // Строим чередующийся путь: шаг по X, шаг по Y, шаг по X, шаг по Y...
        var smooth = new List<(int X, int Y)>();
        int cx = startX, cy = startY;
        int totalX = Math.Abs(dx);
        int totalY = Math.Abs(dy);
        int takenX = 0, takenY = 0;
        bool preferX = true;
        int stuckCounter = 0;

        while (takenX < totalX || takenY < totalY)
        {
            bool stepped = false;

            if (preferX && takenX < totalX)
            {
                int nx = cx + stepX;
                if (!isBlocked(nx, cy))
                {
                    cx = nx; takenX++; smooth.Add((cx, cy)); stepped = true;
                }
            }
            else if (!preferX && takenY < totalY)
            {
                int ny = cy + stepY;
                if (!isBlocked(cx, ny))
                {
                    cy = ny; takenY++; smooth.Add((cx, cy)); stepped = true;
                }
            }

            if (!stepped)
            {
                if (takenX < totalX)
                {
                    int nx = cx + stepX;
                    if (!isBlocked(nx, cy))
                    {
                        cx = nx; takenX++; smooth.Add((cx, cy)); stepped = true;
                    }
                }
                if (!stepped && takenY < totalY)
                {
                    int ny = cy + stepY;
                    if (!isBlocked(cx, ny))
                    {
                        cy = ny; takenY++; smooth.Add((cx, cy)); stepped = true;
                    }
                }
            }

            if (cx == targetX && cy == targetY)
                break;

            if (!stepped)
            {
                stuckCounter++;
                if (stuckCounter > raw.Count * 2)
                    return raw;
            }

            preferX = !preferX;
        }

        if (cx != targetX || cy != targetY)
            return raw;

        return smooth;
    }
}

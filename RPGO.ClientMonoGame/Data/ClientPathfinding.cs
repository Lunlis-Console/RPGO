using RPGGame.Shared.Utils;

namespace RPGGame.ClientMonoGame.Data;

public static class ClientPathfinding
{
    /// <summary>
    /// Поиск пути с обходом препятствий и НЕПРОХОДИМЫХ статичных сущностей.
    /// Путь строится только через пустые клетки — торговец, порталы, инстансы,
    /// сундуки и т.п. не проходной. Игроки не учитываются (через них можно идти).
    /// </summary>
    public static List<(int X, int Y)> FindPath(int sx, int sy, int tx, int ty,
        int worldW, int worldH, HashSet<(int X, int Y)> blockedCells,
        Func<int, int, bool>? isBlocked = null)
    {
        return Shared.Utils.Pathfinding.FindPath(sx, sy, tx, ty, worldW, worldH,
            (nx, ny) =>
                // Клетка назначения достижима даже если там сущность (встаём на неё,
                // чтобы взаимодействовать/активировать портал), а промежуточные клетки-сущности обходим.
                ((nx == tx && ny == ty) ? false :
                (blockedCells.Contains((nx, ny)) ||
                (isBlocked?.Invoke(nx, ny) ?? false))));
    }
}
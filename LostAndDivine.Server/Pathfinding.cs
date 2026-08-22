using LostAndDivine.Server.Instances;
using LostAndDivine.Server.Services;
using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Utils;

namespace LostAndDivine.Server;

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

    private ZoneManager _zones = null!;
    private StorageService _storage = null!;
    private InstanceManager _instances = null!;
    /// <summary>Явная инъекция зависимостей (P3): зоны/склад/инстансы нужны для блокировки клеток.</summary>
    public void Configure(ZoneManager zones, StorageService storage, InstanceManager instances)
    {
        _zones = zones; _storage = storage; _instances = instances;
    }

    public List<(int X, int Y)> FindPath(int startX, int startY, int targetX, int targetY)
        => FindPath(startX, startY, targetX, targetY, Balance.MainZoneId);

    /// <summary>
    /// Поиск пути по клеткам зоны с обходом препятствий и статичных сущностей
    /// (торговец, доска, порталы, NPC, склад, сундуки инстансов). Игроки не учитываются.
    /// Должен совпадать по логике с клиентским ClientPathfinding.
    /// </summary>
    public List<(int X, int Y)> FindPath(int startX, int startY, int targetX, int targetY, string zoneId)
    {
        var zoneMap = _zones != null ? _zones.GetOrCreateMap(zoneId) : _world.Map;
        var blocked = BuildBlockedCells(zoneId);

        return Shared.Utils.Pathfinding.FindPath(startX, startY, targetX, targetY,
            zoneMap.Width, zoneMap.Height,
            (nx, ny) =>
                zoneMap.IsObstacle(nx, ny) ||
                // Клетка назначения достижима даже если там сущность (на неё встаём,
                // чтобы взаимодействовать/активировать портал), а вот промежуточные
                // клетки-сущности обходим.
                ((nx == targetX && ny == targetY) ? false : blocked.Contains((nx, ny))));
    }

    /// <summary>
    /// Клетки статичных сущностей зоны, через которые путь строить нельзя.
    /// Совпадает с клиентским набором в MapRenderer.DrawPathDots.
    /// </summary>
    private HashSet<(int X, int Y)> BuildBlockedCells(string zoneId)
    {
        var blocked = new HashSet<(int X, int Y)>();

        // Торговец и доска (главная зона/по умолчанию).
        blocked.Add((_merchant.MerchantX, _merchant.MerchantY));
        blocked.Add((_quests.BoardX, _quests.BoardY));

        if (_zones != null)
        {
            // Порталы зоны.
            foreach (var p in _zones.GetPortalsForZone(zoneId))
                blocked.Add((p.FromX, p.FromY));

            // Склад (главная зона).
            if (zoneId == Balance.MainZoneId && _storage != null)
                blocked.Add((_storage.StorageX, _storage.StorageY));

            // NPC зоны (из Tiled; позиции авторитетнее, чем в БД).
            foreach (var n in _zones.GetTiledNpcs(zoneId))
                blocked.Add((n.X, n.Y));

            // Сундук и выходной портал текущего инстанса.
            if (zoneId.StartsWith("instance:"))
            {
                var inst = _instances?.FindInstanceByZoneId(zoneId);
                if (inst != null)
                {
                    blocked.Add((inst.EffectiveChestX, inst.EffectiveChestY));
                    blocked.Add((inst.EffectiveExitX, inst.EffectiveExitY));
                }
            }
        }

        return blocked;
    }
}
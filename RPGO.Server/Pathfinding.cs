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

    /// <summary>Полный доступ к сервисам (зоны, порталы, склад NPC). Ставится после создания GameServices.</summary>
    public GameServices? Services { get; set; }

    public List<(int X, int Y)> FindPath(int startX, int startY, int targetX, int targetY)
        => FindPath(startX, startY, targetX, targetY, "main");

    /// <summary>
    /// Поиск пути по клеткам зоны с обходом препятствий и статичных сущностей
    /// (торговец, доска, порталы, NPC, склад, сундуки инстансов). Игроки не учитываются.
    /// Должен совпадать по логике с клиентским ClientPathfinding.
    /// </summary>
    public List<(int X, int Y)> FindPath(int startX, int startY, int targetX, int targetY, string zoneId)
    {
        var zoneMap = Services?.Zones.GetOrCreateMap(zoneId) ?? _world.Map;
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

        if (Services != null)
        {
            // Порталы зоны.
            foreach (var p in Services.Zones.GetPortalsForZone(zoneId))
                blocked.Add((p.FromX, p.FromY));

            // Склад (главная зона).
            if (zoneId == "main")
                blocked.Add((Services.Storage.StorageX, Services.Storage.StorageY));

            // NPC зоны (из Tiled; позиции авторитетнее, чем в БД).
            foreach (var n in Services.Zones.GetTiledNpcs(zoneId))
                blocked.Add((n.X, n.Y));

            // Сундук и выходной портал текущего инстанса.
            if (zoneId.StartsWith("instance:"))
            {
                var inst = Services.Instances.FindInstanceByZoneId(zoneId);
                if (inst != null)
                {
                    blocked.Add((inst.Template.ChestX + inst.OffsetX, inst.Template.ChestY + inst.OffsetY));
                    blocked.Add((inst.Template.ExitX + inst.OffsetX, inst.Template.ExitY + inst.OffsetY));
                }
            }
        }

        return blocked;
    }
}
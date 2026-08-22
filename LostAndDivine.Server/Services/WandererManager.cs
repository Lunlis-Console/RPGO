using LostAndDivine.Server.Network;
using LostAndDivine.Shared.Models;

namespace LostAndDivine.Server.Services;

/// <summary>
/// Блуждающие NPC (тип "wanderer"): мирные жители/заключённые, которые просто ходят
/// по локации, оживляя мир. Не агрятся, не атакуют, не умирают — чисто визуальное движение.
/// Движение — случайный шаг в радиусе вокруг точки спавна, без путей/агро (как блуждание мобов).
/// Позиции публикуются через EntityState (как у мобов), чтобы клиент играл walk-анимацию.
/// </summary>
public sealed class WandererManager
{
    private readonly GameWorld _world;
    private GameServices _svc = null!;
    private readonly object _lock = new();
    private readonly List<WandererState> _wanderers = new();

    public WandererManager(GameWorld world)
    {
        _world = world;
    }

    public void SetServices(GameServices svc) => _svc = svc;

    public void Initialize(List<NpcPosition> wanderers)
    {
        lock (_lock)
        {
            _wanderers.Clear();
            foreach (var w in wanderers)
            {
                _wanderers.Add(new WandererState
                {
                    Id = w.Id,
                    Name = w.Name,
                    ZoneId = w.ZoneId,
                    X = w.X,
                    Y = w.Y,
                    HomeX = w.X,
                    HomeY = w.Y,
                    Facing = w.Facing,
                    Radius = w.WanderRadius,
                    LastMoveTime = DateTime.UtcNow
                });
            }
        }
    }

    /// <summary>Текущие позиции бродяг для рассылки через EntityState.</summary>
    public List<NpcPosition> GetPositions()
    {
        lock (_lock)
        {
            var result = new List<NpcPosition>(_wanderers.Count);
            foreach (var w in _wanderers)
                result.Add(new NpcPosition
                {
                    Id = w.Id,
                    Name = w.Name,
                    Type = "wanderer",
                    X = w.X,
                    Y = w.Y,
                    ZoneId = w.ZoneId,
                    Facing = w.Facing,
                    IsMoving = w.IsMoving
                });
            return result;
        }
    }

    public void WanderStep()
    {
        var now = DateTime.UtcNow;
        lock (_lock)
        {
            foreach (var w in _wanderers)
            {
                if ((now - w.LastMoveTime).TotalMilliseconds < Balance.WandererMoveIntervalMs)
                    continue;
                w.LastMoveTime = now.AddMilliseconds(_world.NextRandom(0, Balance.WandererMoveJitterMaxMs));

                bool wasMoving = w.IsMoving;

                // Радиус 0 (или меньше) — бродяга стоит на месте и не блуждает
                // (0 в редакторе = «не блуждать»). Глобальный Balance.WandererWanderRadius
                // — это значение по умолчанию для новых NPC, а не поведение при 0.
                if (w.Radius <= 0)
                {
                    w.IsMoving = false;
                    if (w.IsMoving != wasMoving)
                        _svc.Hub.MarkEntityStateDirty(w.ZoneId);
                    continue;
                }

                // Карта проверки препятствий — своя для зоны бродяги. _world.Map — это
                // карта последней загруженной зоны, поэтому для бродяг в других зонах
                // она не подходит (они ходили бы сквозь стены).
                var zoneMap = _svc.Zones.GetOrCreateMap(w.ZoneId);
                int mapW = zoneMap.Width, mapH = zoneMap.Height;

                bool moved = false;

                // Шанс постоять на месте (более «живое» поведение).
                if (_world.NextRandom(0, 100) < Balance.WandererWanderSkipChance)
                {
                    w.IsMoving = false;
                }
                else
                {
                    // Собираем только доступные для передвижения соседние клетки:
                    // в границах карты, в радиусе блуждания от точки спавна и проходимые
                    // (не препятствие и не «пустой/недоступный» тайл).
                    var options = new List<(int nx, int ny, string facing)>();
                    for (int dir = 0; dir < 4; dir++)
                    {
                        int dx = dir == 2 ? -1 : dir == 3 ? 1 : 0;
                        int dy = dir == 0 ? -1 : dir == 1 ? 1 : 0;
                        int nx = w.X + dx;
                        int ny = w.Y + dy;
                        if (nx < 0 || nx >= mapW || ny < 0 || ny >= mapH) continue;
                        if (Math.Abs(nx - w.HomeX) > w.Radius || Math.Abs(ny - w.HomeY) > w.Radius) continue;
                        if (!IsWalkable(zoneMap, nx, ny)) continue;
                        string facing = dir == 0 ? "up" : dir == 1 ? "down" : dir == 2 ? "left" : "right";
                        options.Add((nx, ny, facing));
                    }

                    if (options.Count == 0)
                    {
                        w.IsMoving = false;
                    }
                    else
                    {
                        var pick = options[_world.NextRandom(0, options.Count)];
                        w.X = pick.nx;
                        w.Y = pick.ny;
                        w.Facing = pick.facing;
                        w.IsMoving = true;
                        moved = true;
                    }
                }

                // Помечаем зону, если бродяга сдвинулся ИЛИ сменил состояние движения
                // (чтобы клиент вовремя выключил walk-анимацию при остановке).
                if (moved || w.IsMoving != wasMoving)
                    _svc.Hub.MarkEntityStateDirty(w.ZoneId);
            }
        }
    }

    /// <summary>Клетка доступна для передвижения бродяги: в границах, не препятствие
    /// и не «пустой/недоступный» тайл (0 или 255). Если тайлы зоны не загружены,
    /// опираемся только на препятствия (как у игрока).</summary>
    private static bool IsWalkable(GameMap map, int x, int y)
    {
        if (x < 0 || y < 0 || x >= map.Width || y >= map.Height) return false;
        if (map.IsObstacle(x, y)) return false;
        var tiles = map.GetTiles();
        if (tiles != null)
        {
            byte t = map.GetTile(x, y);
            if (t == 0 || t == 255) return false;
        }
        return true;
    }

    private sealed class WandererState
    {
        public string Id = "";
        public string Name = "";
        public string ZoneId = "";
        public int X;
        public int Y;
        public int HomeX;
        public int HomeY;
        public int Radius = Balance.WandererWanderRadius;
        public string Facing = "down";
        public bool IsMoving;
        public DateTime LastMoveTime;
    }
}

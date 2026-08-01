using RPGGame.Server.Repositories;
using RPGGame.Shared.Models;

namespace RPGGame.Server;

/// <summary>
/// Менеджер зон: загружает зоны и порталы из БД, предоставляет GameMap для каждой зоны.
/// </summary>
public class ZoneManager
{
    private readonly Dictionary<string, Zone> _zones = new();
    private readonly Dictionary<string, GameMap> _maps = new();
    private readonly Dictionary<string, GameMap> _instanceMaps = new();
    private readonly List<WorldPortal> _portals = new();
    private readonly Dictionary<(string Zone, int X, int Y), WorldPortal> _portalLookup = new();
    private readonly Dictionary<string, List<WorldPortal>> _portalsByZone = new();
    private readonly Dictionary<string, (int TileWidth, string TilesetId)> _tileConfig = new();
    private GameMap? _mainMap;

    public IReadOnlyDictionary<string, Zone> Zones => _zones;
    public IReadOnlyList<WorldPortal> Portals => _portals;

    /// <summary>
    /// Главная зона использует карту мира (GameWorld.Map): тайлы и препятствия
    /// должны быть общими для рендера, патфайндинга и движения.
    /// </summary>
    public void SetMainMap(GameMap map) => _mainMap = map;

    public void LoadAll()
    {
        _zones.Clear();
        _maps.Clear();
        _portals.Clear();
        _portalLookup.Clear();
        _portalsByZone.Clear();

        foreach (var zone in ZoneRepository.LoadAll())
        {
            _zones[zone.Id] = zone;
            _maps[zone.Id] = new GameMap(zone.Width, zone.Height);
        }

        if (_mainMap != null)
            _maps["main"] = _mainMap;

        foreach (var portal in ZoneRepository.LoadPortals())
        {
            _portals.Add(portal);
            _portalLookup[(portal.FromZone, portal.FromX, portal.FromY)] = portal;

            if (!_portalsByZone.ContainsKey(portal.FromZone))
                _portalsByZone[portal.FromZone] = new List<WorldPortal>();
            _portalsByZone[portal.FromZone].Add(portal);
        }

        Log.Info($"Загружено {_zones.Count} зон, {_portals.Count} порталов");
    }

    public Zone? GetZone(string id) => _zones.TryGetValue(id, out var zone) ? zone : null;

    public GameMap? GetMap(string zoneId)
    {
        if (_maps.TryGetValue(zoneId, out var map)) return map;
        if (_instanceMaps.TryGetValue(zoneId, out var imap)) return imap;
        return null;
    }

    public bool IsPvPEnabled(string zoneId) => _zones.TryGetValue(zoneId, out var zone) && zone.PvpEnabled;

    public WorldPortal? FindPortal(string zone, int x, int y)
        => _portalLookup.TryGetValue((zone, x, y), out var portal) ? portal : null;

    public List<WorldPortal> GetPortalsForZone(string zoneId)
        => _portalsByZone.TryGetValue(zoneId, out var list) ? list : new List<WorldPortal>();

    public IReadOnlyDictionary<string, List<WorldPortal>> GetAllPortalsByZone()
        => _portalsByZone;

    /// <summary>
    /// Получить GameMap для зоны (создаёт дефолтную если зоны нет в БД).
    /// </summary>
    public GameMap GetOrCreateMap(string zoneId)
    {
        if (_maps.TryGetValue(zoneId, out var map))
            return map;
        if (_instanceMaps.TryGetValue(zoneId, out var imap))
            return imap;

        var fallback = new GameMap(Balance.WorldWidth, Balance.WorldHeight);
        _maps[zoneId] = fallback;
        return fallback;
    }

    public void RegisterInstanceZone(string zoneId, GameMap map)
    {
        _instanceMaps[zoneId] = map;
    }

    public void UnregisterInstanceZone(string zoneId)
    {
        _instanceMaps.Remove(zoneId);
    }

    /// <summary>
    /// Регистрирует порталы, размещённые в Tiled-картах (добавляются поверх порталов из БД).
    /// </summary>
    public void RegisterTiledPortals(IEnumerable<WorldPortal> portals)
    {
        foreach (var portal in portals)
        {
            _portals.Add(portal);
            _portalLookup[(portal.FromZone, portal.FromX, portal.FromY)] = portal;

            if (!_portalsByZone.ContainsKey(portal.FromZone))
                _portalsByZone[portal.FromZone] = new List<WorldPortal>();
            _portalsByZone[portal.FromZone].Add(portal);
        }

        var list = portals as IReadOnlyCollection<WorldPortal> ?? portals.ToList();
        if (list.Count > 0)
            Log.Info($"Зарегистрировано порталов из Tiled: {list.Count}");
    }

    /// <summary>
    /// Тайл-конфигурация зоны для рендера на клиенте (размер тайла + id тайлсета).
    /// Задаётся при загрузке Tiled-карты. По умолчанию — 32px и тайлсет по имени зоны (инстансы).
    /// </summary>
    public void SetTileConfig(string zoneId, int tileWidth, string tilesetId)
        => _tileConfig[zoneId] = (tileWidth, tilesetId);

    public (int TileWidth, string TilesetId) GetTileConfig(string zoneId)
        => _tileConfig.TryGetValue(zoneId, out var cfg) ? cfg : (32, zoneId);

    /// <summary>
    /// Создаёт (или заменяет) GameMap зоны нужного размера из Tiled-карты.
    /// Для главной зоны возвращает карту мира и требует совпадения размеров.
    /// Обновляет ширину/высоту зоны в памяти под размер карты.
    /// </summary>
    public GameMap CreateOrReplaceMap(string zoneId, int width, int height)
    {
        if (zoneId == "main" && _mainMap != null)
        {
            if (_mainMap.Width != width || _mainMap.Height != height)
                throw new InvalidOperationException($"Размер Tiled-карты {width}x{height} не совпадает с картой мира {_mainMap.Width}x{_mainMap.Height}");
            return _mainMap;
        }

        var map = new GameMap(width, height);
        _maps[zoneId] = map;
        if (_zones.TryGetValue(zoneId, out var zone))
        {
            zone.Width = width;
            zone.Height = height;
        }
        return map;
    }
}

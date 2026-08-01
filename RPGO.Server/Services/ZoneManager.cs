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
}

using LostAndDivine.Server.Services;
using LostAndDivine.Shared.Models;

namespace LostAndDivine.Server;

/// <summary>
/// Секторный открытый мир (main): сетка SectorCols x SectorRows секторов по
/// SectorSize клеток. Каждый сектор — Content/Sectors/{col}_{row}.tmj с ЛОКАЛЬНЫМИ
/// координатами объектов; глобальная координата = локальная + (col,row) * SectorSize.
/// Тайлы и препятствия всех секторов сливаются в карту мира (GameWorld.Map);
/// NPC, порталы, двери и спавны регистрируются в зоне main с глобальными координатами.
/// Секторы с полностью пустым слоем земли — шаблоны, контента не дают.
/// </summary>
public sealed class SectorWorld
{
    private readonly Dictionary<(int Col, int Row), SectorContent> _sectors = new();

    /// <summary>Загруженный сектор: тайлы/препятствия в ЛОКАЛЬНЫХ координатах сектора.</summary>
    public sealed class SectorContent
    {
        public int Col { get; init; }
        public int Row { get; init; }
        public byte[] Tiles { get; init; } = Array.Empty<byte>();
        public byte[] Obstacles { get; init; } = Array.Empty<byte>();
        public byte[]? Objects { get; init; }
        public int TileWidth { get; init; } = 64;
        public string TilesetId { get; init; } = "World-Tilemap";
        public string? ObjectTilesetId { get; init; }
        public int ObjectTileWidth { get; init; }
        /// <summary>Сектор-шаблон: слой земли полностью пуст (контента нет).</summary>
        public bool IsEmpty { get; init; } = true;
    }

    public IReadOnlyDictionary<(int Col, int Row), SectorContent> Sectors => _sectors;

    public SectorContent? Get(int col, int row)
        => _sectors.TryGetValue((col, row), out var s) ? s : null;

    /// <summary>
    /// Клетка мира находится в секторе с реальным контентом.
    /// Используется для миграции старых сохранённых координат (0..99 → пустой сектор 0_0).
    /// </summary>
    public bool IsValidWorldCell(int x, int y)
    {
        if (x < 0 || x >= Balance.MainWorldWidth || y < 0 || y >= Balance.MainWorldHeight)
            return false;
        var s = Get(x / Balance.SectorSize, y / Balance.SectorSize);
        return s != null && !s.IsEmpty;
    }

    /// <summary>Спавны монстров/коллекционок из всех секторов (глобальные координаты).</summary>
    public List<TiledSpawn> AllSpawns { get; } = new();

    /// <summary>Спавны коллекционок по зонам: main заполняется из секторов (глобальные координаты).</summary>
    public Dictionary<string, List<TiledSpawn>> AllCollectibleSpawns { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Загружает все секторы папки и встраивает их в карту мира и зону main.
    /// Должен вызываться после zones.LoadAll() и до инициализации мерчанта/квестов/монстров.
    /// </summary>
    public void Load(GameMap worldMap, ZoneManager zones, string sectorsDir)
    {
        _sectors.Clear();
        AllSpawns.Clear();
        AllCollectibleSpawns.Clear();

        var allTiles = new byte[Balance.MainWorldWidth * Balance.MainWorldHeight];
        var tiledNpcs = new List<TiledNpc>();
        var tiledPortals = new List<TiledMapLoader.TiledPortal>();
        var tiledDoors = new List<TiledDoor>();
        (int X, int Y)? playerSpawn = null;

        string? tilesetId = null;
        string? objectTilesetId = null;
        int objectTileWidth = 0;
        int tileWidth = 64;

        int loaded = 0;
        int loadedContent = 0;
        foreach (var file in Directory.GetFiles(sectorsDir, "*.tmj", SearchOption.TopDirectoryOnly))
        {
            string fname = Path.GetFileNameWithoutExtension(file);
            if (!TryParseSectorName(fname, out int col, out int row))
            {
                Log.Warn($"Файл сектора '{fname}' не соответствует формату {{col}}_{{row}}, пропущен");
                continue;
            }
            if (col < 0 || col >= Balance.SectorCols || row < 0 || row >= Balance.SectorRows) continue;

            int ox = col * Balance.SectorSize;
            int oy = row * Balance.SectorSize;

            var tiledMap = TiledMapLoader.Load(file);
            var tileData = TiledMapLoader.ExtractTileLayer(tiledMap);
            var obstacles = TiledMapLoader.ExtractObstacles(tiledMap);
            var objectData = TiledMapLoader.ExtractObjectLayer(tiledMap);
            var objectTileset = TiledMapLoader.GetObjectLayerTileset(tiledMap);

            bool empty = true;
            for (int i = 0; i < tileData.Length; i++)
            {
                if (tileData[i] != 0) { empty = false; break; }
            }

            var obstacleData = new byte[Balance.SectorSize * Balance.SectorSize];
            foreach (var (lx, ly) in obstacles)
            {
                if (lx < 0 || ly < 0 || lx >= Balance.SectorSize || ly >= Balance.SectorSize) continue;
                obstacleData[ly * Balance.SectorSize + lx] = 1;
                worldMap.AddObstacle(ox + lx, oy + ly);
            }

            _sectors[(col, row)] = new SectorContent
            {
                Col = col,
                Row = row,
                Tiles = tileData,
                Obstacles = obstacleData,
                Objects = objectData,
                TileWidth = tiledMap.TileWidth,
                TilesetId = tiledMap.Tilesets.Count > 0 ? tiledMap.Tilesets[0].Name : "World-Tilemap",
                ObjectTilesetId = objectTileset?.Name,
                ObjectTileWidth = objectTileset?.TileWidth ?? 0,
                IsEmpty = empty
            };
            loaded++;

            if (empty) continue;
            loadedContent++;

            // Сливаем тайлы сектора в глобальную карту мира
            for (int ly = 0; ly < Balance.SectorSize; ly++)
            {
                int srcBase = ly * Balance.SectorSize;
                int dstBase = (oy + ly) * Balance.MainWorldWidth + ox;
                Buffer.BlockCopy(tileData, srcBase, allTiles, dstBase, Balance.SectorSize);
            }

            if (tilesetId == null)
            {
                tilesetId = _sectors[(col, row)].TilesetId;
                objectTilesetId = objectTileset?.Name;
                objectTileWidth = objectTileset?.TileWidth ?? 0;
                tileWidth = tiledMap.TileWidth;
            }

            foreach (var s in TiledMapLoader.ExtractSpawns(tiledMap))
                AllSpawns.Add(new TiledSpawn(ox + s.X, oy + s.Y, s.Name, s.Type));

            foreach (var n in TiledMapLoader.ExtractNpcs(tiledMap, Balance.MainZoneId))
                tiledNpcs.Add(n with { X = ox + n.X, Y = oy + n.Y });

            var portals = TiledMapLoader.ExtractPortals(tiledMap, toZone =>
            {
                var targetZone = zones.GetZone(toZone);
                return targetZone != null ? ((int X, int Y)?)(targetZone.SpawnX, targetZone.SpawnY) : null;
            });
            foreach (var p in portals)
            {
                int toX = p.ToX, toY = p.ToY;
                if (string.Equals(p.ToZone, Balance.MainZoneId, StringComparison.OrdinalIgnoreCase))
                {
                    // Портал в открытый мир: координаты в свойствах заданы в старой
                    // локальной системе zone_main → переводим в глобальные секторного мира.
                    toX += Balance.EntrySectorOffsetX;
                    toY += Balance.EntrySectorOffsetY;
                }
                tiledPortals.Add(new TiledMapLoader.TiledPortal(ox + p.X, oy + p.Y, p.ToZone, toX, toY));
            }

            foreach (var d in TiledMapLoader.ExtractDoors(tiledMap))
                tiledDoors.Add(new TiledDoor(ox + d.X, oy + d.Y, d.Name));

            var spawnPoint = TiledMapLoader.ExtractPlayerSpawn(tiledMap);
            if (spawnPoint != null)
                playerSpawn = (ox + spawnPoint.Value.X, oy + spawnPoint.Value.Y);
        }

        Log.Info($"Секторы: загружено {loaded}, с контентом: {loadedContent}");

        worldMap.SetTiles(allTiles);
        zones.ConfigureMainZone(playerSpawn ?? (Balance.EntrySpawnX, Balance.EntrySpawnY));

        if (tiledNpcs.Count > 0)
            zones.RegisterTiledNpcs(Balance.MainZoneId, tiledNpcs);

        if (tiledPortals.Count > 0)
        {
            zones.RegisterTiledPortals(tiledPortals.Select(p => new WorldPortal
            {
                Id = $"sector_{p.X}_{p.Y}",
                FromZone = Balance.MainZoneId,
                FromX = p.X,
                FromY = p.Y,
                ToZone = p.ToZone,
                ToX = p.ToX,
                ToY = p.ToY
            }));
        }

        if (tiledDoors.Count > 0)
        {
            zones.RegisterDoors(Balance.MainZoneId, tiledDoors);
            foreach (var door in tiledDoors)
            {
                if (!worldMap.IsObstacle(door.X, door.Y))
                    worldMap.AddObstacle(door.X, door.Y);
            }
            Log.Info($"Дверей в зоне 'main': {tiledDoors.Count}");
        }

        if (tilesetId != null)
            zones.SetTileConfig(Balance.MainZoneId, tileWidth, tilesetId, objectTilesetId, objectTileWidth);

        AllCollectibleSpawns[Balance.MainZoneId] = AllSpawns;
    }

    /// <summary>Имя сектора "{col}_{row}" → (col, row).</summary>
    private static bool TryParseSectorName(string name, out int col, out int row)
    {
        col = 0;
        row = 0;
        int sep = name.IndexOf('_');
        if (sep <= 0 || sep == name.Length - 1) return false;
        if (!int.TryParse(name.Substring(0, sep), out col)) return false;
        if (!int.TryParse(name.Substring(sep + 1), out row)) return false;
        return true;
    }
}

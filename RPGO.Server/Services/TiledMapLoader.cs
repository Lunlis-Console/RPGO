using System.Text.Json;
using RPGGame.Shared.Models;

namespace RPGGame.Server.Services;

public class TiledMapData
{
    public int Width { get; set; }
    public int Height { get; set; }
    public int TileWidth { get; set; }
    public int TileHeight { get; set; }
    public List<TiledLayer> Layers { get; set; } = new();
    public List<TiledTileset> Tilesets { get; set; } = new();
}

public class TiledLayer
{
    public List<long> Data { get; set; } = new();
    public int Width { get; set; }
    public int Height { get; set; }
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public bool Visible { get; set; } = true;
    public List<TiledObject> Objects { get; set; } = new();
}

public class TiledObject
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public bool Point { get; set; }
    public List<TiledProperty> Properties { get; set; } = new();
}

public class TiledProperty
{
    public string Name { get; set; } = "";
    public JsonElement Value { get; set; }
}

public class TiledTileset
{
    public int FirstGid { get; set; }
    public string Image { get; set; } = "";
    public int ImageWidth { get; set; }
    public int ImageHeight { get; set; }
    public int Columns { get; set; }
    public int TileCount { get; set; }
    public int TileWidth { get; set; }
    public int TileHeight { get; set; }
    public string Name { get; set; } = "";
}

/// <summary>Точка спавна из Tiled: координата в тайлах + имя сущности (шаблон монстра / коллекционка).</summary>
public record TiledSpawn(int X, int Y, string Name, string Type);

/// <summary>Позиция NPC из Tiled: координата в тайлах, имя (id записи npcs или instance_template_id) и тип.</summary>
public record TiledNpc(int X, int Y, string Name, string Type, string ZoneId);

/// <summary>Точки для данж-карт: вход игрока, спавны монстров/босса, сундук, выход.</summary>
public record DungeonSpawnData(
    (int X, int Y) PlayerSpawn,
    List<(int X, int Y)> MonsterSpawns,
    (int X, int Y) BossSpawn,
    (int X, int Y) Chest,
    (int X, int Y) Exit);

public static class TiledMapLoader
{
    private const uint FlippedHorizontally = 0x80000000;
    private const uint FlippedVertically = 0x40000000;
    private const uint FlippedDiagonally = 0x20000000;
    private const uint GidMask = 0x1FFFFFFF;

    /// <summary>Типы Tiled-объектов, относящиеся к NPC/спец-объектам, а не к спавнам монстров.</summary>
    private static readonly HashSet<string> NonSpawnTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "portal", "npc", "merchant", "board", "instance_portal", "dummy"
    };

    public static TiledMapData Load(string jsonPath)
    {
        var json = File.ReadAllText(jsonPath);
        var map = JsonSerializer.Deserialize<TiledMapData>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return map ?? throw new Exception("Failed to parse Tiled JSON map");
    }

    public static byte[] ExtractTileLayer(TiledMapData map, int layerIndex = 0)
    {
        if (layerIndex < 0 || layerIndex >= map.Layers.Count)
            throw new Exception($"Layer {layerIndex} not found");

        var layer = map.Layers[layerIndex];
        int count = layer.Width * layer.Height;
        var tiles = new byte[count];

        int firstGid = map.Tilesets.Count > 0 ? map.Tilesets[0].FirstGid : 1;
        int tileCount = map.Tilesets.Count > 0 ? map.Tilesets[0].TileCount : 0;
        int cols = map.Tilesets.Count > 0 ? map.Tilesets[0].Columns : 1;
        int maxGid = firstGid + tileCount - 1;

        for (int i = 0; i < count && i < layer.Data.Count; i++)
        {
            uint gid = (uint)(layer.Data[i] & 0xFFFFFFFF);
            uint rawTile = gid & GidMask;

            // Игнорируем тайлы вне диапазона известного тайлсета
            if (rawTile == 0 || rawTile < firstGid || rawTile > maxGid)
            {
                tiles[i] = 0;
                continue;
            }

            // Tiled GID → local tile index (0-based in tileset)
            int localId = (int)(rawTile - firstGid);

            // Game byte encoding: 1-254=tileset tile index+1, 255=void
            if (localId >= 254)
                tiles[i] = 255;
            else
                tiles[i] = (byte)(localId + 1);
        }

        // Лог тайла (49,49) для отладки
        int checkIdx = 49 + 49 * 100;
        if (checkIdx < layer.Data.Count)
        {
            long raw = layer.Data[checkIdx];
            uint gid = (uint)(raw & 0xFFFFFFFF);
            uint rawTile = gid & GidMask;
            int localId = rawTile >= firstGid ? (int)(rawTile - firstGid) : -1;
            int col = localId >= 0 ? localId % cols : -1;
            int row = localId >= 0 ? localId / cols : -1;
            Log.Debug($"  Тайл[49,49]: rawGID={raw} maskedGID={rawTile} localId={localId} tileId={tiles[checkIdx]} → колона={col} ряд={row}");
        }

        return tiles;
    }

    /// <summary>Имя тайлового слоя «объекты поверх карты» (деревья, камни и т.п.).</summary>
    public static readonly string ObjectLayerName = "Объекты на карте";

    /// <summary>
    /// Извлекает слой объектов (тайлы, рисуемые поверх сущностей, например деревья).
    /// Кодировка та же, что у слоя земли: 0 — пусто, 1..254 — локальный индекс тайла + 1,
    /// 255 — вне тайлсета. Возвращает null, если слоя нет.
    /// </summary>
    public static byte[]? ExtractObjectLayer(TiledMapData map)
    {
        var layer = map.Layers.FirstOrDefault(l =>
            l.Visible &&
            string.Equals(l.Type, "tilelayer", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(l.Name, ObjectLayerName, StringComparison.OrdinalIgnoreCase));
        if (layer == null) return null;

        int count = layer.Width * layer.Height;
        var tiles = new byte[count];
        for (int i = 0; i < count && i < layer.Data.Count; i++)
        {
            uint rawTile = (uint)(layer.Data[i] & 0xFFFFFFFF) & GidMask;
            if (rawTile == 0) continue;

            var ts = FindTileset(map, rawTile);
            if (ts == null) continue;

            int localId = (int)(rawTile - ts.FirstGid);
            tiles[i] = localId >= 254 ? (byte)255 : (byte)(localId + 1);
        }
        return tiles;
    }

    /// <summary>
    /// Тайлсет слоя объектов (второй тайлсет карты, напр. Tileset-Tree). null — слоя нет.
    /// </summary>
    public static TiledTileset? GetObjectLayerTileset(TiledMapData map)
    {
        if (map.Layers.All(l =>
            !(string.Equals(l.Type, "tilelayer", StringComparison.OrdinalIgnoreCase) &&
              string.Equals(l.Name, ObjectLayerName, StringComparison.OrdinalIgnoreCase))))
            return null;
        return map.Tilesets.Count > 1 ? map.Tilesets[1] : null;
    }

    private static TiledTileset? FindTileset(TiledMapData map, uint gid)
    {
        TiledTileset? best = null;
        foreach (var ts in map.Tilesets)
        {
            if (gid >= (uint)ts.FirstGid && (best == null || ts.FirstGid > best.FirstGid))
                best = ts;
        }
        return best;
    }

    /// <summary>
    /// Извлекает препятствия из object-слоёв (objectgroup): каждый прямоугольник
    /// превращается в набор тайловых координат, перекрываемых этим прямоугольником.
    /// </summary>
    public static List<(int X, int Y)> ExtractObstacles(TiledMapData map)
    {
        var obstacles = new List<(int X, int Y)>();
        if (map.TileWidth <= 0 || map.TileHeight <= 0) return obstacles;

        foreach (var layer in map.Layers)
        {
            if (!layer.Visible || !string.Equals(layer.Type, "objectgroup", StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var obj in layer.Objects)
            {
                if (obj.Point || obj.Width <= 0 || obj.Height <= 0) continue;
                // Именованные объекты — это точки спавна, а не препятствия
                if (!string.IsNullOrEmpty(obj.Name) || !string.IsNullOrEmpty(obj.Type)) continue;

                int x0 = (int)Math.Floor(obj.X / map.TileWidth);
                int y0 = (int)Math.Floor(obj.Y / map.TileHeight);
                int x1 = (int)Math.Ceiling((obj.X + obj.Width) / map.TileWidth);
                int y1 = (int)Math.Ceiling((obj.Y + obj.Height) / map.TileHeight);

                for (int ty = y0; ty < y1; ty++)
                for (int tx = x0; tx < x1; tx++)
                {
                    if (tx >= 0 && ty >= 0 && tx < map.Width && ty < map.Height)
                        obstacles.Add((tx, ty));
                }
            }
        }
        return obstacles;
    }

    /// <summary>
    /// Извлекает точки спавна из object-слоёв. Точкой считается объект с заполненным
    /// name (или type): для point-объекта берётся его координата, для прямоугольника — центр.
    /// </summary>
    public static List<TiledSpawn> ExtractSpawns(TiledMapData map)
    {
        var result = new List<TiledSpawn>();
        if (map.TileWidth <= 0 || map.TileHeight <= 0) return result;

        foreach (var layer in map.Layers)
        {
            if (!layer.Visible || !string.Equals(layer.Type, "objectgroup", StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var obj in layer.Objects)
            {
                // Порталы и NPC-объекты обрабатываются отдельно (ExtractPortals / ExtractNpcs), а не как точки спавна
                if (NonSpawnTypes.Contains(obj.Type ?? ""))
                    continue;

                if (string.IsNullOrEmpty(obj.Name) && string.IsNullOrEmpty(obj.Type))
                    continue;

                int tx, ty;
                if (obj.Point)
                {
                    tx = (int)Math.Floor(obj.X / map.TileWidth);
                    ty = (int)Math.Floor(obj.Y / map.TileHeight);
                }
                else if (obj.Width > 0 && obj.Height > 0)
                {
                    tx = (int)Math.Floor((obj.X + obj.Width / 2) / map.TileWidth);
                    ty = (int)Math.Floor((obj.Y + obj.Height / 2) / map.TileHeight);
                }
                else continue;

                if (tx < 0 || ty < 0 || tx >= map.Width || ty >= map.Height) continue;
                result.Add(new TiledSpawn(tx, ty, obj.Name ?? "", obj.Type ?? ""));
            }
        }
        return result;
    }

    /// <summary>Портал из Tiled: позиция в текущей зоне + целевая зона и координаты.</summary>
    public record TiledPortal(int X, int Y, string ToZone, int ToX, int ToY);    /// <summary>
    /// Извлекает порталы из object-слоёв. Объект считается порталом, если type == "portal":
    /// name — id целевой зоны, свойства to_x/to_y — координаты в ней (иначе спавн целевой зоны).
    /// </summary>
    public static List<TiledPortal> ExtractPortals(TiledMapData map, Func<string, (int X, int Y)?>? spawnFallback = null)
    {
        var result = new List<TiledPortal>();
        if (map.TileWidth <= 0 || map.TileHeight <= 0) return result;

        foreach (var layer in map.Layers)
        {
            if (!layer.Visible || !string.Equals(layer.Type, "objectgroup", StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var obj in layer.Objects)
            {
                if (!string.Equals(obj.Type, "portal", StringComparison.OrdinalIgnoreCase))
                    continue;

                string toZone = obj.Name;
                if (string.IsNullOrEmpty(toZone)) continue;

                int fromX, fromY;
                if (obj.Point)
                {
                    fromX = (int)Math.Floor(obj.X / map.TileWidth);
                    fromY = (int)Math.Floor(obj.Y / map.TileHeight);
                }
                else if (obj.Width > 0 && obj.Height > 0)
                {
                    fromX = (int)Math.Floor((obj.X + obj.Width / 2) / map.TileWidth);
                    fromY = (int)Math.Floor((obj.Y + obj.Height / 2) / map.TileHeight);
                }
                else continue;

                if (fromX < 0 || fromY < 0 || fromX >= map.Width || fromY >= map.Height) continue;

                int toX = GetPropertyInt(obj, "to_x") ?? 0;
                int toY = GetPropertyInt(obj, "to_y") ?? 0;
                if (toX == 0 && toY == 0 && spawnFallback != null)
                {
                    var spawn = spawnFallback(toZone);
                    if (spawn != null)
                    {
                        toX = spawn.Value.X;
                        toY = spawn.Value.Y;
                    }
                }

                result.Add(new TiledPortal(fromX, fromY, toZone, toX, toY));
            }
        }
        return result;
    }

    /// <summary>
    /// Извлекает позиции NPC из object-слоёв. NPC-объектом считается объект с типом
    /// из NonSpawnTypes (npc/merchant/board/instance_portal/dummy): name — id записи npcs
    /// (для instance_portal — id шаблона инстанса). Для point-объекта берётся его
    /// координата, для прямоугольника — центр.
    /// </summary>
    public static List<TiledNpc> ExtractNpcs(TiledMapData map, string zoneId)
    {
        var result = new List<TiledNpc>();
        if (map.TileWidth <= 0 || map.TileHeight <= 0) return result;

        foreach (var layer in map.Layers)
        {
            if (!layer.Visible || !string.Equals(layer.Type, "objectgroup", StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var obj in layer.Objects)
            {
                if (string.Equals(obj.Type, "portal", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!NonSpawnTypes.Contains(obj.Type ?? ""))
                    continue;

                int tx, ty;
                if (obj.Point)
                {
                    tx = (int)Math.Floor(obj.X / map.TileWidth);
                    ty = (int)Math.Floor(obj.Y / map.TileHeight);
                }
                else if (obj.Width > 0 && obj.Height > 0)
                {
                    tx = (int)Math.Floor((obj.X + obj.Width / 2) / map.TileWidth);
                    ty = (int)Math.Floor((obj.Y + obj.Height / 2) / map.TileHeight);
                }
                else continue;

                if (tx < 0 || ty < 0 || tx >= map.Width || ty >= map.Height) continue;
                result.Add(new TiledNpc(tx, ty, obj.Name ?? "", obj.Type ?? "", zoneId));
            }
        }
        return result;
    }

    private static int? GetPropertyInt(TiledObject obj, string name)
    {
        foreach (var prop in obj.Properties)
        {
            if (!string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            if (prop.Value.ValueKind == JsonValueKind.Number)
                return prop.Value.GetInt32();
            if (prop.Value.ValueKind == JsonValueKind.String &&
                int.TryParse(prop.Value.GetString(), out int parsed))
                return parsed;
            return null;
        }
        return null;
    }

    /// <summary>
    /// Извлекает точки для данж-карт из object-слоёв.
    /// Ищет объекты с типами: player_spawn, monster_spawn, boss_spawn, chest, exit_portal.
    /// </summary>
    public static DungeonSpawnData? ExtractDungeonObjects(TiledMapData map)
    {
        if (map.TileWidth <= 0 || map.TileHeight <= 0) return null;

        (int X, int Y)? playerSpawn = null;
        var monsterSpawns = new List<(int X, int Y)>();
        (int X, int Y)? bossSpawn = null;
        (int X, int Y)? chest = null;
        (int X, int Y)? exit = null;

        foreach (var layer in map.Layers)
        {
            if (!layer.Visible || !string.Equals(layer.Type, "objectgroup", StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var obj in layer.Objects)
            {
                var type = obj.Type ?? "";
                int tx, ty;
                if (obj.Point)
                {
                    tx = (int)Math.Floor(obj.X / map.TileWidth);
                    ty = (int)Math.Floor(obj.Y / map.TileHeight);
                }
                else if (obj.Width > 0 && obj.Height > 0)
                {
                    tx = (int)Math.Floor((obj.X + obj.Width / 2) / map.TileWidth);
                    ty = (int)Math.Floor((obj.Y + obj.Height / 2) / map.TileHeight);
                }
                else continue;

                if (tx < 0 || ty < 0 || tx >= map.Width || ty >= map.Height) continue;

                switch (type.ToLowerInvariant())
                {
                    case "player_spawn": playerSpawn = (tx, ty); break;
                    case "monster_spawn": monsterSpawns.Add((tx, ty)); break;
                    case "boss_spawn": bossSpawn = (tx, ty); break;
                    case "chest": chest = (tx, ty); break;
                    case "exit_portal": exit = (tx, ty); break;
                }
            }
        }

        if (playerSpawn == null || bossSpawn == null || chest == null || exit == null)
            return null;

        return new DungeonSpawnData(playerSpawn.Value, monsterSpawns, bossSpawn.Value, chest.Value, exit.Value);
    }

    public static string GetTilesetPngPath(TiledMapData map, string baseDir)
    {
        if (map.Tilesets.Count == 0) return "";
        var ts = map.Tilesets[0];
        return Path.GetFullPath(Path.Combine(baseDir, ts.Image));
    }
}

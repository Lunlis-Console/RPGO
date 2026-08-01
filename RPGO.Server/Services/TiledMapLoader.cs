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

public static class TiledMapLoader
{
    private const uint FlippedHorizontally = 0x80000000;
    private const uint FlippedVertically = 0x40000000;
    private const uint FlippedDiagonally = 0x20000000;
    private const uint GidMask = 0x1FFFFFFF;

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

    public static string GetTilesetPngPath(TiledMapData map, string baseDir)
    {
        if (map.Tilesets.Count == 0) return "";
        var ts = map.Tilesets[0];
        return Path.GetFullPath(Path.Combine(baseDir, ts.Image));
    }
}

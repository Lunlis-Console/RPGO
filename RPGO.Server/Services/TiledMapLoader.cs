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

    public static string GetTilesetPngPath(TiledMapData map, string baseDir)
    {
        if (map.Tilesets.Count == 0) return "";
        var ts = map.Tilesets[0];
        return Path.GetFullPath(Path.Combine(baseDir, ts.Image));
    }
}

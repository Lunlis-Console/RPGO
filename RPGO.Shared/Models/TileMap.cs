namespace RPGGame.Shared.Models;

public class TileMap
{
    public string Id { get; set; } = "";
    public string ZoneId { get; set; } = BalanceStatic.MainZoneId;
    public int Width { get; set; } = 100;
    public int Height { get; set; } = 100;
    public int TileSize { get; set; } = 32;
    public string TilesetId { get; set; } = "";
    public byte[] Tiles { get; set; } = Array.Empty<byte>();

    public byte GetTile(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return (byte)TileType.Null;
        return Tiles[y * Width + x];
    }

    public void SetTile(int x, int y, byte tileType)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return;
        Tiles[y * Width + x] = tileType;
    }
}
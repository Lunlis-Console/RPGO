using RPGGame.Server.Services;

namespace RPGO.Tests;

public class TiledMapLoaderTests
{
    [Fact]
    public void ExtractObjectLayer_Wordlmap_HasTreesInTreeTilesetRange()
    {
        string mapPath = Path.Combine(AppContext.BaseDirectory, "zone_main.tmj");
        Assert.True(File.Exists(mapPath), $"Карта не найдена: {mapPath}");

        var map = TiledMapLoader.Load(mapPath);
        var objectLayer = TiledMapLoader.ExtractObjectLayer(map);
        Assert.NotNull(objectLayer);
        Assert.Equal(map.Width * map.Height, objectLayer!.Length);

        int treeCount = objectLayer.Count(t => t != 0);
        Assert.True(treeCount > 0, "В слое объектов должны быть размещённые деревья");

        // Валидные тайлы кодируются как локальный индекс + 1 и не должны выходить за пределы
        Assert.All(objectLayer, t => Assert.InRange(t, (byte)0, (byte)254));

        // Тайлсет слоя объектов — второй тайлсет карты (Tileset-Tree)
        var tileset = TiledMapLoader.GetObjectLayerTileset(map);
        Assert.NotNull(tileset);
        Assert.Equal("Tileset-Tree", tileset!.Name);
        Assert.Equal(64, tileset.TileWidth);
    }
}

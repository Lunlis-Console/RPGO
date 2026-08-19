using System.Text.Json;
using LostAndDivine.Shared.Tiled;

namespace LostAndDivine.Tests;

public class TiledNpcWriterTests
{
    // Формат Tiled: CRLF, по свойству на строку, ключи по алфавиту.
    private const string MapWithNpcLayer = """
        {
         "height":10,
         "infinite":false,
         "layers":[
                {
                 "data":[0,0],
                 "height":10,
                 "id":1,
                 "name":"\u0421\u043b\u043e\u0439 \u0442\u0430\u0439\u043b\u043e\u0432 1",
                 "opacity":1,
                 "type":"tilelayer",
                 "visible":true,
                 "width":10,
                 "x":0,
                 "y":0
                }, 
                {
                 "draworder":"topdown",
                 "id":2,
                 "name":"NPC",
                 "objects":[
                        {
                         "height":0,
                         "id":5,
                         "name":"N0001",
                         "opacity":1,
                         "point":true,
                         "rotation":0,
                         "type":"merchant",
                         "visible":true,
                         "width":0,
                         "x":3200,
                         "y":3200
                        },
                        {
                         "height":0,
                         "id":6,
                         "name":"N0003",
                         "opacity":1,
                         "point":true,
                         "rotation":0,
                         "type":"npc",
                         "visible":true,
                         "width":0,
                         "x":3072,
                         "y":3328
                        }],
                 "opacity":1,
                 "type":"objectgroup",
                 "visible":true,
                 "x":0,
                 "y":0
                }],
         "nextlayerid":3,
         "nextobjectid":7,
         "orientation":"orthogonal",
         "renderorder":"right-down",
         "tiledversion":"1.12.2",
         "tileheight":64,
         "tilesets":[
                {
                 "columns":30,
                 "firstgid":1,
                 "image":"Tilesets\/World-Tilemap.png",
                 "imageheight":512,
                 "imagewidth":960,
                 "margin":0,
                 "name":"World-Tilemap",
                 "spacing":0,
                 "tilecount":480,
                 "tileheight":64,
                 "tilewidth":64
                }],
         "tilewidth":64,
         "type":"map",
         "version":"1.10",
         "width":10
        }
        """;

    private const string MapWithoutNpcLayer = """
        {
         "height":10,
         "infinite":false,
         "layers":[
                {
                 "data":[0,0],
                 "height":10,
                 "id":1,
                 "name":"\u0421\u043b\u043e\u0439 \u0442\u0430\u0439\u043b\u043e\u0432 1",
                 "opacity":1,
                 "type":"tilelayer",
                 "visible":true,
                 "width":10,
                 "x":0,
                 "y":0
                }, 
                {
                 "draworder":"topdown",
                 "id":2,
                 "name":"\u041f\u043e\u0440\u0442\u0430\u043b\u044b",
                 "objects":[
                        {
                         "height":64,
                         "id":4,
                         "name":"airship",
                         "opacity":1,
                         "rotation":0,
                         "type":"portal",
                         "visible":true,
                         "width":64,
                         "x":2304,
                         "y":1600
                        }],
                 "opacity":1,
                 "type":"objectgroup",
                 "visible":true,
                 "x":0,
                 "y":0
                }],
         "nextlayerid":3,
         "nextobjectid":5,
         "orientation":"orthogonal",
         "renderorder":"right-down",
         "tiledversion":"1.12.2",
         "tileheight":64,
         "tilesets":[
                {
                 "columns":30,
                 "firstgid":1,
                 "image":"Tilesets\/World-Tilemap.png",
                 "imageheight":512,
                 "imagewidth":960,
                 "margin":0,
                 "name":"World-Tilemap",
                 "spacing":0,
                 "tilecount":480,
                 "tileheight":64,
                 "tilewidth":64
                }],
         "tilewidth":64,
         "type":"map",
         "version":"1.10",
         "width":10
        }
        """;

    private static string Crlf(string fixture) => fixture.Replace("\r\n", "\n").Replace("\n", "\r\n");

    private static string WriteTemp(string fixture)
    {
        var file = Path.Combine(Path.GetTempPath(), $"npcwriter_{Guid.NewGuid():N}.tmj");
        File.WriteAllText(file, Crlf(fixture));
        return file;
    }

    private static List<(string Name, int X, int Y, string Type)> ReadNpcObjects(string file)
    {
        var result = new List<(string, int, int, string)>();
        using var doc = JsonDocument.Parse(File.ReadAllText(file));
        foreach (var layer in doc.RootElement.GetProperty("layers").EnumerateArray())
        {
            if (!layer.TryGetProperty("objects", out var objs)) continue;
            foreach (var o in objs.EnumerateArray())
            {
                string name = o.GetProperty("name").GetString() ?? "";
                string type = o.TryGetProperty("type", out var t) ? (t.GetString() ?? "") : "";
                if (type is "npc" or "merchant" or "board" or "instance_portal" or "dummy" or "storage")
                    result.Add((name, o.GetProperty("x").GetInt32(), o.GetProperty("y").GetInt32(), type));
            }
        }
        return result;
    }

    private static int Counter(string file, string key)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(file));
        return doc.RootElement.GetProperty(key).GetInt32();
    }

    [Fact]
    public void Upsert_NewObject_AddsToNpcLayer()
    {
        var file = WriteTemp(MapWithNpcLayer);
        var r = TiledNpcWriter.Upsert(file, "N0002", "board", 10, 20, 64, 64);
        Assert.True(r.Added);
        Assert.False(r.Moved);

        var objs = ReadNpcObjects(file);
        var npc = Assert.Single(objs, o => o.Name == "N0002");
        Assert.Equal((640, 1280), (npc.X, npc.Y));
        Assert.Equal("board", npc.Type);
        Assert.Equal(3, objs.Count);
        Assert.Equal(8, Counter(file, "nextobjectid"));
        Assert.Equal(3, Counter(file, "nextlayerid"));
        // ключи объекта по алфавиту и формат Tiled сохранены
        var text = File.ReadAllText(file);
        Assert.Contains("\"id\":7,\r\n", text);
        Assert.Contains("\"point\":true,\r\n", text);
    }

    [Fact]
    public void Upsert_Move_UpdatesExistingPosition()
    {
        var file = WriteTemp(MapWithNpcLayer);
        var r = TiledNpcWriter.Upsert(file, "N0001", "merchant", 1, 2, 64, 64);
        Assert.False(r.Added);
        Assert.True(r.Moved);

        var objs = ReadNpcObjects(file);
        Assert.Single(objs, o => o.Name == "N0001");
        var npc = objs.First(o => o.Name == "N0001");
        Assert.Equal((64, 128), (npc.X, npc.Y));
        Assert.Equal(2, objs.Count);
        Assert.Equal(8, Counter(file, "nextobjectid"));
    }

    [Fact]
    public void Upsert_ChangesType()
    {
        var file = WriteTemp(MapWithNpcLayer);
        TiledNpcWriter.Upsert(file, "N0003", "npc", 5, 5, 64, 64);
        var npc = ReadNpcObjects(file).First(o => o.Name == "N0003");
        Assert.Equal("npc", npc.Type);
    }

    [Fact]
    public void Upsert_NoNpcLayer_CreatesLayerBeforeNextlayerid()
    {
        var file = WriteTemp(MapWithoutNpcLayer);
        var r = TiledNpcWriter.Upsert(file, "N0004", "npc", 3, 4, 64, 64);
        Assert.True(r.Added);

        var objs = ReadNpcObjects(file);
        var npc = Assert.Single(objs, o => o.Name == "N0004");
        Assert.Equal((192, 256), (npc.X, npc.Y));
        Assert.Equal(4, Counter(file, "nextlayerid"));
        Assert.Equal(6, Counter(file, "nextobjectid"));
        var text = File.ReadAllText(file);
        Assert.Contains("\"name\":\"NPC\",\r\n", text);
        // портал на месте, формат не сломан
        Assert.Contains("\"name\":\"airship\"", text);
    }

    [Fact]
    public void Remove_LastObject_NormalizesEmptyArray()
    {
        var single = Crlf(MapWithNpcLayer)
            .Replace(
                "                },\r\n                {\r\n                 \"height\":0,\r\n                 \"id\":6,\r\n                 \"name\":\"N0003\",\r\n                 \"opacity\":1,\r\n                 \"point\":true,\r\n                 \"rotation\":0,\r\n                 \"type\":\"npc\",\r\n                 \"visible\":true,\r\n                 \"width\":0,\r\n                 \"x\":3072,\r\n                 \"y\":3328\r\n                }],",
                "                }],");
        var file = WriteTemp(single);
        Assert.True(TiledNpcWriter.Remove(file, "N0001"));
        Assert.Empty(ReadNpcObjects(file));
        using var doc = JsonDocument.Parse(File.ReadAllText(file)); // JSON валиден
        Assert.Equal("[]", doc.RootElement.GetProperty("layers")[1].GetProperty("objects").ToString());
    }

    [Fact]
    public void Remove_MiddleObject_KeepsArrayValid()
    {
        var file = WriteTemp(MapWithNpcLayer);
        Assert.True(TiledNpcWriter.Remove(file, "N0001"));
        var objs = ReadNpcObjects(file);
        var npc = Assert.Single(objs);
        Assert.Equal("N0003", npc.Name);
        Assert.Equal(3072, npc.X);
        var text = File.ReadAllText(file);
        Assert.Contains("\"name\":\"N0003\"", text);
        Assert.DoesNotContain("\"name\":\"N0001\"", text);
    }

    [Fact]
    public void Remove_RemovesDuplicates()
    {
        var dup = Crlf(MapWithNpcLayer).Replace(
            "                 \"x\":3200,\r\n                 \"y\":3200\r\n                },",
            "                 \"x\":3200,\r\n                 \"y\":3200\r\n                },\r\n                {\r\n                 \"height\":0,\r\n                 \"id\":99,\r\n                 \"name\":\"N0001\",\r\n                 \"opacity\":1,\r\n                 \"point\":true,\r\n                 \"rotation\":0,\r\n                 \"type\":\"merchant\",\r\n                 \"visible\":true,\r\n                 \"width\":0,\r\n                 \"x\":1,\r\n                 \"y\":1\r\n                },");
        var file = WriteTemp(dup);
        Assert.True(TiledNpcWriter.Remove(file, "N0001"));
        Assert.DoesNotContain(ReadNpcObjects(file), o => o.Name == "N0001");
    }

    [Fact]
    public void Upsert_RemovesDuplicatesAndMoves()
    {
        var dup = Crlf(MapWithNpcLayer).Replace(
            "                 \"x\":3200,\r\n                 \"y\":3200\r\n                },",
            "                 \"x\":3200,\r\n                 \"y\":3200\r\n                },\r\n                {\r\n                 \"height\":0,\r\n                 \"id\":99,\r\n                 \"name\":\"N0001\",\r\n                 \"opacity\":1,\r\n                 \"point\":true,\r\n                 \"rotation\":0,\r\n                 \"type\":\"merchant\",\r\n                 \"visible\":true,\r\n                 \"width\":0,\r\n                 \"x\":1,\r\n                 \"y\":1\r\n                },");
        var file = WriteTemp(dup);
        var r = TiledNpcWriter.Upsert(file, "N0001", "merchant", 7, 8, 64, 64);
        Assert.True(r.Moved);
        var objs = ReadNpcObjects(file).Where(o => o.Name == "N0001").ToList();
        var npc = Assert.Single(objs);
        Assert.Equal((448, 512), (npc.X, npc.Y));
    }

    [Fact]
    public void RemoveFromAllMaps_ScansZonesAndSectors()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"npcdir_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(dir, "Sectors"));
        var zoneFile = Path.Combine(dir, "zone_airship.tmj");
        var sectorFile = Path.Combine(dir, "Sectors", "3_7.tmj");
        var otherFile = Path.Combine(dir, "zone_arena.tmj");
        File.WriteAllText(zoneFile, Crlf(MapWithNpcLayer));
        File.WriteAllText(sectorFile, Crlf(MapWithNpcLayer));
        File.WriteAllText(otherFile, Crlf(MapWithoutNpcLayer));

        int removed = TiledNpcWriter.RemoveFromAllMaps(dir, "N0001");
        Assert.Equal(2, removed);
        Assert.DoesNotContain(ReadNpcObjects(zoneFile), o => o.Name == "N0001");
        Assert.DoesNotContain(ReadNpcObjects(sectorFile), o => o.Name == "N0001");
        Assert.Equal(0, TiledNpcWriter.RemoveFromAllMaps(dir, "N0001"));
    }

    [Fact]
    public void Remove_WhenAbsent_ReturnsFalse()
    {
        var file = WriteTemp(MapWithNpcLayer);
        Assert.False(TiledNpcWriter.Remove(file, "N0999"));
    }
}
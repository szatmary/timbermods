using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using Xunit;
using MapGen;

namespace MapGen.Tests;

public class MapSerializerTests
{
    [Fact]
    public void Write_produces_zip_with_four_entries()
    {
        var map = MakeMinimalMap();
        var path = TempPath();
        try
        {
            MapSerializer.Write(map, path, "1.0.13.0-test");
            using var fs = File.OpenRead(path);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
            var names = zip.Entries.Select(e => e.FullName).OrderBy(n => n).ToArray();
            Assert.Equal(new[] { "map_metadata.json", "map_thumbnail.jpg", "version.txt", "world.json" }, names);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Write_includes_supplied_thumbnail()
    {
        var map = MakeMinimalMap();
        var custom = new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 };
        var path = TempPath();
        try
        {
            MapSerializer.Write(map, path, "1.0.13.0-test", custom);
            using var fs = File.OpenRead(path);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
            var entry = zip.GetEntry("map_thumbnail.jpg")!;
            using var s = entry.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            Assert.Equal(custom, ms.ToArray());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Write_with_null_thumbnail_uses_stub()
    {
        var map = MakeMinimalMap();
        var path = TempPath();
        try
        {
            MapSerializer.Write(map, path, "1.0.13.0-test");
            using var fs = File.OpenRead(path);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
            var entry = zip.GetEntry("map_thumbnail.jpg")!;
            Assert.True(entry.Length > 0 && entry.Length < 2048);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Write_metadata_size_matches_map()
    {
        var map = MakeMinimalMap(64, 32);
        var path = TempPath();
        try
        {
            MapSerializer.Write(map, path, "1.0.13.0-test");
            using var fs = File.OpenRead(path);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
            var entry = zip.GetEntry("map_metadata.json")!;
            using var s = entry.Open();
            using var doc = JsonDocument.Parse(s);
            Assert.Equal(64, doc.RootElement.GetProperty("Width").GetInt32());
            Assert.Equal(32, doc.RootElement.GetProperty("Height").GetInt32());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Write_version_txt_matches_supplied_string()
    {
        var map = MakeMinimalMap();
        var path = TempPath();
        try
        {
            MapSerializer.Write(map, path, "1.0.13.0-1e60728-xsm");
            using var fs = File.OpenRead(path);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
            var entry = zip.GetEntry("version.txt")!;
            using var s = entry.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            Assert.Equal("1.0.13.0-1e60728-xsm", Encoding.UTF8.GetString(ms.ToArray()));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void WorldJson_terrain_voxel_count_matches_W_H_FixedZ23()
    {
        var map = MakeMinimalMap(16, 16);
        var path = TempPath();
        try
        {
            MapSerializer.Write(map, path, "1.0.13.0-test");
            using var fs = File.OpenRead(path);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
            using var s = zip.GetEntry("world.json")!.Open();
            using var doc = JsonDocument.Parse(s);
            var arr = doc.RootElement
                .GetProperty("Singletons")
                .GetProperty("TerrainMap")
                .GetProperty("Voxels")
                .GetProperty("Array")
                .GetString()!;
            var tokens = arr.Split(' ');
            int expected = 16 * 16 * 23;  // Timberborn fixed Z=23
            Assert.Equal(expected, tokens.Length);
            int onesInFirstFour = tokens.Take(16 * 16 * 4).Count(t => t == "1");
            Assert.Equal(16 * 16 * 4, onesInFirstFour);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void WorldJson_top_level_has_required_fields()
    {
        var map = MakeMinimalMap();
        var path = TempPath();
        try
        {
            MapSerializer.Write(map, path, "1.0.13.0-test");
            using var fs = File.OpenRead(path);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
            using var s = zip.GetEntry("world.json")!.Open();
            using var doc = JsonDocument.Parse(s);
            var root = doc.RootElement;
            Assert.Equal("1.0.13.0-test", root.GetProperty("GameVersion").GetString());
            Assert.True(root.TryGetProperty("Timestamp", out _));
            Assert.True(root.TryGetProperty("Singletons", out _));
            Assert.True(root.TryGetProperty("Entities", out _));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void WorldJson_entity_per_placed()
    {
        var map = MakeMinimalMap();
        map.Entities.Add(new PlacedEntity("Pine", new VoxelCoord(5, 5, 5), Orientation.North, EntityKind.Tree));
        map.Entities.Add(new PlacedEntity("BlueberryBush", new VoxelCoord(7, 7, 5), Orientation.North, EntityKind.Resource));
        map.Entities.Add(new PlacedEntity("StartingLocation", new VoxelCoord(10, 10, 5), Orientation.North, EntityKind.StartMarker));

        var path = TempPath();
        try
        {
            MapSerializer.Write(map, path, "1.0.13.0-test");
            using var fs = File.OpenRead(path);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
            using var s = zip.GetEntry("world.json")!.Open();
            using var doc = JsonDocument.Parse(s);
            var entities = doc.RootElement.GetProperty("Entities");
            Assert.Equal(3, entities.GetArrayLength());
            Assert.Equal("Pine", entities[0].GetProperty("Template").GetString());
            Assert.Equal("BlueberryBush", entities[1].GetProperty("Template").GetString());
            Assert.Equal("StartingLocation", entities[2].GetProperty("Template").GetString());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void WorldJson_tree_has_components()
    {
        var map = MakeMinimalMap();
        map.Entities.Add(new PlacedEntity("Pine", new VoxelCoord(5, 5, 5), Orientation.North, EntityKind.Tree));
        var path = TempPath();
        try
        {
            MapSerializer.Write(map, path, "1.0.13.0-test");
            using var fs = File.OpenRead(path);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
            using var s = zip.GetEntry("world.json")!.Open();
            using var doc = JsonDocument.Parse(s);
            var components = doc.RootElement.GetProperty("Entities")[0].GetProperty("Components");
            Assert.True(components.TryGetProperty("BlockObject", out _));
            Assert.True(components.TryGetProperty("LivingNaturalResource", out _));
            Assert.True(components.TryGetProperty("Growable", out _));
            Assert.True(components.TryGetProperty("Yielder:Cuttable", out _));
            Assert.True(components.TryGetProperty("Yielder:Gatherable", out _));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void WorldJson_water_source_has_strength()
    {
        var map = MakeMinimalMap();
        map.Entities.Add(new PlacedEntity("WaterSource", new VoxelCoord(3, 3, 5),
            Orientation.North, EntityKind.WaterSource, 1.5f));
        var path = TempPath();
        try
        {
            MapSerializer.Write(map, path, "1.0.13.0-test");
            using var fs = File.OpenRead(path);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
            using var s = zip.GetEntry("world.json")!.Open();
            using var doc = JsonDocument.Parse(s);
            var ws = doc.RootElement.GetProperty("Entities")[0]
                .GetProperty("Components").GetProperty("WaterSource");
            Assert.Equal(1.5f, ws.GetProperty("SpecifiedStrength").GetSingle());
            Assert.Equal(1.5f, ws.GetProperty("CurrentStrength").GetSingle());
        }
        finally { File.Delete(path); }
    }

    private static MapData MakeMinimalMap(int w = 32, int h = 32)
    {
        var map = new MapData(w, h, "TEST");
        map.MetaWidth = w / 8;
        map.MetaHeight = h / 8;
        map.Biomes = new Biome[map.MetaWidth * map.MetaHeight];
        map.Columns = new System.Collections.Generic.List<VoxelSpan>[w * h];
        for (int i = 0; i < map.Columns.Length; i++)
            map.Columns[i] = new System.Collections.Generic.List<VoxelSpan> { new VoxelSpan(0, 4) };
        map.WaterDepths = new byte[w * h];
        return map;
    }

    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"mapgen-test-{System.Guid.NewGuid():N}.timber");
}

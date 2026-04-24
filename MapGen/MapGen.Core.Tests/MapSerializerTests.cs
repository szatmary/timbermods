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

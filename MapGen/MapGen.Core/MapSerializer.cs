using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace MapGen;

public static class MapSerializer
{
    /// Writes `map` to `path` as a Timberborn-compatible .timber ZIP file.
    /// `gameVersion` is written verbatim into version.txt and world.json.
    /// `thumbnailJpeg` if null embeds a 1x1 stub JPEG.
    public static void Write(MapData map, string path, string gameVersion,
        byte[]? thumbnailJpeg = null)
    {
        if (map is null) throw new ArgumentNullException(nameof(map));
        if (string.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));
        if (gameVersion is null) throw new ArgumentNullException(nameof(gameVersion));

        if (File.Exists(path)) File.Delete(path);
        using var fs = File.Create(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false);

        WriteEntry(zip, "version.txt", s => WriteVersionTxt(s, gameVersion));
        WriteEntry(zip, "map_metadata.json", s => WriteMapMetadata(s, map));
        WriteEntry(zip, "map_thumbnail.jpg", s =>
        {
            byte[] bytes = thumbnailJpeg ?? StubJpegBytes;
            s.Write(bytes, 0, bytes.Length);
        });
        WriteEntry(zip, "world.json", s => WriteWorldJson(s, map, gameVersion));
    }

    private static void WriteEntry(ZipArchive zip, string name, Action<Stream> writer)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var s = entry.Open();
        writer(s);
    }

    private static void WriteVersionTxt(Stream s, string gameVersion)
    {
        var bytes = Encoding.UTF8.GetBytes(gameVersion);
        s.Write(bytes, 0, bytes.Length);
    }

    private static void WriteMapMetadata(Stream s, MapData map)
    {
        using var w = new Utf8JsonWriter(s, new JsonWriterOptions { Indented = false });
        w.WriteStartObject();
        w.WriteNumber("Width", map.Width);
        w.WriteNumber("Height", map.Height);
        w.WriteString("MapNameLocKey", "");
        w.WriteString("MapDescriptionLocKey", "");
        w.WriteString("MapDescription", "");
        w.WriteBoolean("IsRecommended", false);
        w.WriteBoolean("IsUnconventional", false);
        w.WriteBoolean("IsDev", false);
        w.WriteEndObject();
    }

    private static void WriteWorldJson(Stream s, MapData map, string gameVersion)
    {
        // Stub for now — Tasks 9-11 fill this in.
        using var w = new Utf8JsonWriter(s, new JsonWriterOptions { Indented = false });
        w.WriteStartObject();
        w.WriteString("GameVersion", gameVersion);
        w.WriteString("Timestamp", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
        w.WriteStartObject("Singletons");
        w.WriteEndObject();
        w.WriteStartArray("Entities");
        w.WriteEndArray();
        w.WriteEndObject();
    }

    private static readonly byte[] StubJpegBytes = HexToBytes(
        "ffd8ffe000104a46494600010100000100010000ffdb004300080606070605080707070909080a0c140d0c0b0b0c1912130f141d1a1f1e1d1a1c1c20242e2720222c231c1c2837292c30313434341f27393d38323c2e333432ffdb0043010909090c0b0c180d0d1832211c213232323232323232323232323232323232323232323232323232323232323232323232323232323232323232323232323232323232ffc00011080001000103012200021101031101ffc4001f0000010501010101010100000000000000000102030405060708090a0bffc400b5100002010303020403050504040000017d01020300041105122131410613516107227114328191a1082342b1c11552d1f02433627282090a161718191a25262728292a3435363738393a434445464748494a535455565758595a636465666768696a737475767778797a838485868788898a92939495969798999aa2a3a4a5a6a7a8a9aab2b3b4b5b6b7b8b9bac2c3c4c5c6c7c8c9cad2d3d4d5d6d7d8d9dae1e2e3e4e5e6e7e8e9eaf1f2f3f4f5f6f7f8f9faffc4001f0100030101010101010101010000000000000102030405060708090a0bffc400b51100020102040403040705040400010277000102031104052131061241510761711322328108144291a1b1c109233352f0156272d10a162434e125f11718191a262728292a35363738393a434445464748494a535455565758595a636465666768696a737475767778797a82838485868788898a92939495969798999aa2a3a4a5a6a7a8a9aab2b3b4b5b6b7b8b9bac2c3c4c5c6c7c8c9cad2d3d4d5d6d7d8d9dae2e3e4e5e6e7e8e9eaf2f3f4f5f6f7f8f9faffda000c03010002110311003f00fbfeffd9");

    private static byte[] HexToBytes(string hex)
    {
        var bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = byte.Parse(hex.Substring(i * 2, 2), System.Globalization.NumberStyles.HexNumber);
        return bytes;
    }
}

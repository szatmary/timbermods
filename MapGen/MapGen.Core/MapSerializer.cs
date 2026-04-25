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
        using var w = new Utf8JsonWriter(s, new JsonWriterOptions { Indented = false });
        w.WriteStartObject();
        w.WriteString("GameVersion", gameVersion);
        w.WriteString("Timestamp", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
        WriteSingletons(w, map);
        WriteEntities(w, map);
        w.WriteEndObject();
    }

    private static readonly byte[] StubJpegBytes = HexToBytes(
        "ffd8ffe000104a46494600010100000100010000ffdb004300080606070605080707070909080a0c140d0c0b0b0c1912130f141d1a1f1e1d1a1c1c20242e2720222c231c1c2837292c30313434341f27393d38323c2e333432ffdb0043010909090c0b0c180d0d1832211c213232323232323232323232323232323232323232323232323232323232323232323232323232323232323232323232323232323232ffc00011080001000103012200021101031101ffc4001f0000010501010101010100000000000000000102030405060708090a0bffc400b5100002010303020403050504040000017d01020300041105122131410613516107227114328191a1082342b1c11552d1f02433627282090a161718191a25262728292a3435363738393a434445464748494a535455565758595a636465666768696a737475767778797a838485868788898a92939495969798999aa2a3a4a5a6a7a8a9aab2b3b4b5b6b7b8b9bac2c3c4c5c6c7c8c9cad2d3d4d5d6d7d8d9dae1e2e3e4e5e6e7e8e9eaf1f2f3f4f5f6f7f8f9faffc4001f0100030101010101010101010000000000000102030405060708090a0bffc400b51100020102040403040705040400010277000102031104052131061241510761711322328108144291a1b1c109233352f0156272d10a162434e125f11718191a262728292a35363738393a434445464748494a535455565758595a636465666768696a737475767778797a82838485868788898a92939495969798999aa2a3a4a5a6a7a8a9aab2b3b4b5b6b7b8b9bac2c3c4c5c6c7c8c9cad2d3d4d5d6d7d8d9dae2e3e4e5e6e7e8e9eaf2f3f4f5f6f7f8f9faffda000c03010002110311003f00fbfeffd9");

    // -------------------------------------------------------------------------
    // Singletons
    // -------------------------------------------------------------------------

    private static void WriteSingletons(Utf8JsonWriter w, MapData map)
    {
        w.WriteStartObject("Singletons");

        w.WriteStartObject("MapSize");
        w.WriteStartObject("Size");
        w.WriteNumber("X", map.Width);
        w.WriteNumber("Y", map.Height);
        w.WriteEndObject();
        w.WriteEndObject();

        WriteTerrainMap(w, map);
        WriteWaterMapNew(w, map);
        WriteWaterEvaporationMap(w, map);
        WriteWaterSimulationMigrator(w);
        WriteSoilMoistureSimulator(w, map);
        WriteSoilContaminationSimulator(w, map);
        WriteHazardousWeatherHistory(w);
        WriteNumberedEntityNamerService(w);
        WriteWindService(w);
        WriteMapThumbnailCameraMover(w);

        w.WriteEndObject();
    }

    private static void WriteTerrainMap(Utf8JsonWriter w, MapData map)
    {
        int zmax = ComputeMaxZ(map);
        w.WriteStartObject("TerrainMap");
        w.WriteStartObject("Voxels");
        w.WriteString("Array", BuildVoxelArrayString(map, zmax));
        w.WriteEndObject();
        w.WriteEndObject();
    }

    /// Timberborn's MapSizeSpec.MaxMapEditorTerrainHeight is fixed at 23.
    /// The game's MapIndexService.Unpack3D expects exactly W*H*23 voxels
    /// regardless of how tall the actual terrain is.
    private const int FixedTerrainZ = 23;

    private static int ComputeMaxZ(MapData map) => FixedTerrainZ;

    /// Layout assumption: z-major, then y-major, then x. The implementer
    /// MUST verify by comparing against a known reference sample. If the
    /// game refuses to load with this layout, swap to (y, x, z) or (x, y, z).
    private static string BuildVoxelArrayString(MapData map, int zmax)
    {
        var sb = new StringBuilder(map.Width * map.Height * zmax * 2);
        bool first = true;
        for (int z = 0; z < zmax; z++)
        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
        {
            if (!first) sb.Append(' ');
            first = false;
            sb.Append(IsSolidAt(map, x, y, z) ? '1' : '0');
        }
        return sb.ToString();
    }

    private static bool IsSolidAt(MapData map, int x, int y, int z)
    {
        var spans = map.Columns[map.ColumnIndex(x, y)];
        for (int i = 0; i < spans.Count; i++)
        {
            var span = spans[i];
            if (z >= span.Bottom && z < span.TopExclusive) return true;
        }
        return false;
    }

    private static byte[] HexToBytes(string hex)
    {
        var bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = byte.Parse(hex.Substring(i * 2, 2), System.Globalization.NumberStyles.HexNumber);
        return bytes;
    }

    private static string ZeroArrayString(int count)
    {
        var sb = new StringBuilder(count * 2);
        for (int i = 0; i < count; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append('0');
        }
        return sb.ToString();
    }

    private static void WriteWaterMapNew(Utf8JsonWriter w, MapData map)
    {
        int n = map.Width * map.Height;
        const int Levels = 2;
        int len = n * Levels;
        w.WriteStartObject("WaterMapNew");
        w.WriteNumber("Levels", Levels);
        w.WriteStartObject("WaterColumns");
        w.WriteString("Array", ZeroArrayString(len));
        w.WriteEndObject();
        w.WriteStartObject("ColumnOutflows");
        w.WriteString("Array", ZeroArrayString(len));
        w.WriteEndObject();
        w.WriteEndObject();
    }

    private static void WriteWaterEvaporationMap(Utf8JsonWriter w, MapData map)
    {
        int n = map.Width * map.Height;
        w.WriteStartObject("WaterEvaporationMap");
        w.WriteNumber("Levels", 1);
        w.WriteStartObject("EvaporationModifiers");
        w.WriteString("Array", ZeroArrayString(n));
        w.WriteEndObject();
        w.WriteEndObject();
    }

    private static void WriteWaterSimulationMigrator(Utf8JsonWriter w)
    {
        w.WriteStartObject("WaterSimulationMigrator");
        w.WriteBoolean("IsMigrated", true);
        w.WriteEndObject();
    }

    private static void WriteSoilMoistureSimulator(Utf8JsonWriter w, MapData map)
    {
        const int Size = 2;
        int len = map.Width * map.Height * Size;
        w.WriteStartObject("SoilMoistureSimulator");
        w.WriteNumber("Size", Size);
        w.WriteStartObject("MoistureLevels");
        w.WriteString("Array", ZeroArrayString(len));
        w.WriteEndObject();
        w.WriteEndObject();
    }

    private static void WriteSoilContaminationSimulator(Utf8JsonWriter w, MapData map)
    {
        const int Size = 2;
        int len = map.Width * map.Height * Size;
        w.WriteStartObject("SoilContaminationSimulator");
        w.WriteNumber("Size", Size);
        w.WriteStartObject("ContaminationCandidates");
        w.WriteString("Array", ZeroArrayString(len));
        w.WriteEndObject();
        w.WriteStartObject("ContaminationLevels");
        w.WriteString("Array", ZeroArrayString(len));
        w.WriteEndObject();
        w.WriteEndObject();
    }

    private static void WriteHazardousWeatherHistory(Utf8JsonWriter w)
    {
        w.WriteStartObject("HazardousWeatherHistory");
        w.WriteStartArray("HistoryData");
        w.WriteEndArray();
        w.WriteEndObject();
    }

    private static void WriteNumberedEntityNamerService(Utf8JsonWriter w)
    {
        w.WriteStartObject("NumberedEntityNamerService");
        w.WriteStartArray("NextNumbers");
        w.WriteEndArray();
        w.WriteEndObject();
    }

    private static void WriteWindService(Utf8JsonWriter w)
    {
        w.WriteStartObject("WindService");
        w.WriteNumber("WindStrength", 0.0f);
        w.WriteStartObject("WindDirection");
        w.WriteNumber("X", 0.0f);
        w.WriteNumber("Y", 0.0f);
        w.WriteEndObject();
        w.WriteNumber("NextWindChangeTime", 0.0f);
        w.WriteEndObject();
    }

    private static void WriteMapThumbnailCameraMover(Utf8JsonWriter w)
    {
        w.WriteStartObject("MapThumbnailCameraMover");
        w.WriteStartObject("CurrentConfiguration");
        w.WriteEndObject();
        w.WriteEndObject();
    }

    // -------------------------------------------------------------------------
    // Entities
    // -------------------------------------------------------------------------

    private static void WriteEntities(Utf8JsonWriter w, MapData map)
    {
        w.WriteStartArray("Entities");
        for (int i = 0; i < map.Entities.Count; i++)
        {
            var e = map.Entities[i];
            w.WriteStartObject();
            w.WriteString("Id", System.Guid.NewGuid().ToString());
            w.WriteString("Template", e.BlueprintKey);
            WriteComponents(w, e);
            w.WriteEndObject();
        }
        w.WriteEndArray();
    }

    private static void WriteComponents(Utf8JsonWriter w, PlacedEntity e)
    {
        w.WriteStartObject("Components");

        w.WriteStartObject("BlockObject");
        w.WriteStartObject("Coordinates");
        w.WriteNumber("X", e.Coord.X);
        w.WriteNumber("Y", e.Coord.Y);
        w.WriteNumber("Z", e.Coord.Z);
        w.WriteEndObject();
        if (e.Facing != Orientation.North)
            w.WriteString("Orientation", FacingToOrientation(e.Facing));
        w.WriteEndObject();

        switch (e.Kind)
        {
            case EntityKind.Tree:
                WriteCoordinatesOffsetter(w);
                WriteLivingNaturalResource(w, isDead: false);
                WriteGrowable(w, 1.0f);
                WriteYielderCuttable(w, "Log", 2);
                if (e.BlueprintKey == "Pine") WriteYielderGatherable(w, "PineResin", 0);
                break;
            case EntityKind.Resource:
                WriteCoordinatesOffsetter(w);
                WriteLivingNaturalResource(w, isDead: false);
                WriteYielderGatherable(w, "Berries", 6);
                break;
            case EntityKind.Ruin:
                WriteYielderRuin(w, "ScrapMetal", 45);
                WriteRuinModels(w, "A");
                break;
            case EntityKind.UnstableCore:
                WriteTimeActivatedComponent(w, isEnabled: true);
                w.WriteStartObject("UnstableCore");
                w.WriteNumber("ExplosionRadius", 5);
                w.WriteEndObject();
                break;
            case EntityKind.WaterSource:
            case EntityKind.BadwaterSource:
                w.WriteStartObject("WaterSource");
                w.WriteNumber("SpecifiedStrength", e.Param > 0 ? e.Param : 1.0f);
                w.WriteNumber("CurrentStrength", e.Param > 0 ? e.Param : 1.0f);
                w.WriteEndObject();
                WriteTimeActivatedComponent(w, isEnabled: false);
                if (e.BlueprintKey == "WaterSeep" || e.BlueprintKey == "BadwaterSeep")
                {
                    w.WriteStartObject("WaterDepthStrengthModifier");
                    w.WriteNumber("CurrentModifier", 0.0f);
                    w.WriteEndObject();
                }
                break;
            // Thorn / Slope / Blockage / Relic / GeothermalVent / StartMarker:
            // BlockObject only — no extra components.
        }
        w.WriteEndObject();  // Components
    }

    private static void WriteCoordinatesOffsetter(Utf8JsonWriter w)
    {
        w.WriteStartObject("CoordinatesOffsetter");
        w.WriteBoolean("Random", true);
        w.WriteEndObject();
    }

    private static void WriteLivingNaturalResource(Utf8JsonWriter w, bool isDead)
    {
        w.WriteStartObject("LivingNaturalResource");
        w.WriteBoolean("IsDead", isDead);
        w.WriteEndObject();
    }

    private static void WriteGrowable(Utf8JsonWriter w, float progress)
    {
        w.WriteStartObject("Growable");
        w.WriteNumber("GrowthProgress", progress);
        w.WriteEndObject();
    }

    private static void WriteYielderCuttable(Utf8JsonWriter w, string good, int amount)
    {
        w.WriteStartObject("Yielder:Cuttable");
        w.WriteStartObject("Yield");
        w.WriteString("Good", good);
        w.WriteNumber("Amount", amount);
        w.WriteEndObject();
        w.WriteEndObject();
    }

    private static void WriteYielderGatherable(Utf8JsonWriter w, string good, int amount)
    {
        w.WriteStartObject("Yielder:Gatherable");
        w.WriteStartObject("Yield");
        w.WriteString("Good", good);
        w.WriteNumber("Amount", amount);
        w.WriteEndObject();
        w.WriteEndObject();
    }

    private static void WriteYielderRuin(Utf8JsonWriter w, string good, int amount)
    {
        w.WriteStartObject("Yielder:Ruin");
        w.WriteStartObject("Yield");
        w.WriteString("Good", good);
        w.WriteNumber("Amount", amount);
        w.WriteEndObject();
        w.WriteEndObject();
    }

    private static void WriteRuinModels(Utf8JsonWriter w, string variantId)
    {
        w.WriteStartObject("RuinModels");
        w.WriteString("VariantId", variantId);
        w.WriteEndObject();
    }

    private static void WriteTimeActivatedComponent(Utf8JsonWriter w, bool isEnabled)
    {
        w.WriteStartObject("TimeActivatedComponent");
        w.WriteBoolean("IsEnabled", isEnabled);
        w.WriteNumber("CyclesUntilCountdownActivation", 5);
        w.WriteNumber("DaysUntilActivation", 10.0f);
        w.WriteNumber("DaysPassed", 0.0f);
        w.WriteEndObject();
    }

    private static string FacingToOrientation(Orientation o) => o switch
    {
        Orientation.East => "Cw90",
        Orientation.South => "Cw180",
        Orientation.West => "Cw270",
        _ => "Cw0",
    };
}

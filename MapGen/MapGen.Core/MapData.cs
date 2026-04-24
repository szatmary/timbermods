using System.Collections.Generic;

namespace MapGen;

/// The generated map. Pipeline stages mutate this as they run.
/// All arrays are sized up-front in MapGenerator before any stage runs.
public sealed class MapData
{
    public MapData(int width, int height, uint seed)
    {
        Width = width;
        Height = height;
        Seed = seed;
    }

    public int Width { get; }
    public int Height { get; }
    public uint Seed { get; }

    /// Metacell biome grid. Size: MetaWidth * MetaHeight. Indexed by
    /// metacell coords: `Biomes[mx + my * MetaWidth]`.
    public Biome[] Biomes = null!;
    public int MetaWidth { get; internal set; }
    public int MetaHeight { get; internal set; }

    /// Which metacell (if any) is the Start. Null means pipeline hasn't
    /// picked yet; after StartSelection this is always non-null.
    public GridCoord? StartMeta;

    /// Per-voxel-column list of solid spans. Length = Width * Height.
    /// Index by `Columns[x + y * Width]`. Each column has at least one
    /// span; most have exactly one `(0, height)`. Rocky overhangs add
    /// a second floating span.
    public List<VoxelSpan>[] Columns = null!;

    /// Water depth per voxel column (top surface of water). 0 means no
    /// water. Positive = standing water above the topmost solid span.
    public byte[] WaterDepths = null!;

    /// Placed entities. Each entity has a blueprint key (catalog id), a
    /// voxel coord, an orientation, and optional per-kind metadata.
    public readonly List<PlacedEntity> Entities = new();
}

public enum Orientation : byte { North, East, South, West }

public readonly struct PlacedEntity
{
    public readonly string BlueprintKey;
    public readonly VoxelCoord Coord;
    public readonly Orientation Facing;
    public readonly EntityKind Kind;
    public readonly float Param;  // Flow rate for water sources, etc.

    public PlacedEntity(string blueprintKey, VoxelCoord coord, Orientation facing,
        EntityKind kind, float param = 0f)
    {
        BlueprintKey = blueprintKey;
        Coord = coord;
        Facing = facing;
        Kind = kind;
        Param = param;
    }
}

public enum EntityKind : byte
{
    Tree,
    Resource,
    Thorn,
    Ruin,
    Blockage,
    Relic,
    UnstableCore,
    GeothermalVent,
    Slope,
    WaterSource,
    BadwaterSource,
    StartMarker,
}

public static class MapDataExtensions
{
    public static int ColumnIndex(this MapData map, int x, int y) => x + y * map.Width;
    public static int ColumnIndex(this MapData map, GridCoord c) => c.X + c.Y * map.Width;
    public static int MetaIndex(this MapData map, int mx, int my) => mx + my * map.MetaWidth;
    public static int MetaIndex(this MapData map, GridCoord c) => c.X + c.Y * map.MetaWidth;

    /// Top solid z at (x, y), from the uppermost span. −1 if no solid span.
    public static int TopHeight(this MapData map, int x, int y)
    {
        var spans = map.Columns[map.ColumnIndex(x, y)];
        if (spans.Count == 0) return -1;
        int top = int.MinValue;
        for (int i = 0; i < spans.Count; i++)
            if (spans[i].TopExclusive > top) top = spans[i].TopExclusive;
        return top - 1;
    }
}

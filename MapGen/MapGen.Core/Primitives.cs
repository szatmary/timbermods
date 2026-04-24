using System;

namespace MapGen;

/// Deterministic, reseedable RNG. xorshift32 — cheap, predictable, no
/// dependency on .NET's System.Random (which differs between runtimes).
public struct Rng
{
    private uint _state;

    public Rng(uint seed)
    {
        // 0 is a fixed point of xorshift; ensure we never start there.
        _state = seed == 0u ? 0x9E3779B9u : seed;
    }

    public uint NextUInt()
    {
        var x = _state;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        _state = x;
        return x;
    }

    /// Returns a float in [0, 1).
    public float NextFloat() => (NextUInt() & 0x00FFFFFFu) / (float)(1 << 24);

    /// Returns an int in [min, max).
    public int NextRange(int min, int max)
    {
        if (max <= min) return min;
        return min + (int)(NextUInt() % (uint)(max - min));
    }

    /// Returns true with the given probability (0..1).
    public bool NextBool(float probability) => NextFloat() < probability;

    /// Weighted pick: indices[i] has weight weights[i]. Returns i.
    public int WeightedPick(float[] weights)
    {
        float total = 0f;
        for (int i = 0; i < weights.Length; i++) total += weights[i];
        var target = NextFloat() * total;
        float cumulative = 0f;
        for (int i = 0; i < weights.Length; i++)
        {
            cumulative += weights[i];
            if (target < cumulative) return i;
        }
        return weights.Length - 1;
    }
}

/// 2D integer coordinate. Used for metacells and voxel columns alike.
public readonly struct GridCoord : IEquatable<GridCoord>
{
    public readonly int X;
    public readonly int Y;

    public GridCoord(int x, int y) { X = x; Y = y; }

    public bool Equals(GridCoord other) => X == other.X && Y == other.Y;
    public override bool Equals(object? obj) => obj is GridCoord gc && Equals(gc);
    public override int GetHashCode() => (X * 73856093) ^ (Y * 19349663);
    public override string ToString() => $"({X},{Y})";

    public static bool operator ==(GridCoord a, GridCoord b) => a.Equals(b);
    public static bool operator !=(GridCoord a, GridCoord b) => !a.Equals(b);
}

/// 3D integer coordinate. Timberborn uses (x, y, z) with z as height.
public readonly struct VoxelCoord : IEquatable<VoxelCoord>
{
    public readonly int X;
    public readonly int Y;
    public readonly int Z;

    public VoxelCoord(int x, int y, int z) { X = x; Y = y; Z = z; }

    public bool Equals(VoxelCoord other) => X == other.X && Y == other.Y && Z == other.Z;
    public override bool Equals(object? obj) => obj is VoxelCoord vc && Equals(vc);
    public override int GetHashCode() =>
        (X * 73856093) ^ (Y * 19349663) ^ (Z * 83492791);
    public override string ToString() => $"({X},{Y},{Z})";
}

/// Contiguous range of solid voxels in a column: solid from Bottom (inclusive)
/// to Bottom+Height (exclusive). A column's list is sorted by Bottom.
public readonly struct VoxelSpan : IEquatable<VoxelSpan>
{
    public readonly int Bottom;
    public readonly int Height;

    public VoxelSpan(int bottom, int height)
    {
        Bottom = bottom;
        Height = height;
    }

    /// Topmost solid z (inclusive). Example: Bottom=0, Height=5 → Top=4.
    public int Top => Bottom + Height - 1;
    /// Exclusive top (i.e. the z where air starts).
    public int TopExclusive => Bottom + Height;

    public bool Equals(VoxelSpan other) => Bottom == other.Bottom && Height == other.Height;
    public override bool Equals(object? obj) => obj is VoxelSpan vs && Equals(vs);
    public override int GetHashCode() => (Bottom * 31) ^ Height;
    public override string ToString() => $"[{Bottom}..{Top}]";
}

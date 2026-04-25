using System;
using System.Collections.Generic;

namespace MapGen;

public static class Heightmap
{
    private readonly struct Profile
    {
        public readonly float BaseHeight;
        public readonly float Amplitude;
        public readonly int Octaves;
        public readonly float Frequency;

        public Profile(float b, float a, int o, float f)
        { BaseHeight = b; Amplitude = a; Octaves = o; Frequency = f; }
    }

    private static Profile ProfileFor(Biome b) => b switch
    {
        Biome.Sea     => new Profile(1f, 1f, 2, 0.05f),
        Biome.Meadow  => new Profile(4f, 2f, 3, 0.08f),
        Biome.Forest  => new Profile(5f, 3f, 4, 0.10f),
        Biome.Badland => new Profile(6f, 4f, 3, 0.06f),
        Biome.Rocky   => new Profile(14f, 6f, 5, 0.18f),
        Biome.Start   => new Profile(4f, 0f, 1, 0.01f),
        _             => new Profile(4f, 2f, 3, 0.08f),
    };

    private const int MetaSize = 8;
    private const int BlendRadius = 2;
    private const int StartApronWidth = 3;

    public static void Build(MapData map, ref Rng rng)
    {
        uint noiseSeed = rng.NextUInt();
        var craters = BuildCraterInfo(map, ref rng);

        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
        {
            float height = SampleHeight(map, x, y, noiseSeed, craters);
            int zTop = Math.Max(1, (int)MathF.Round(height));
            var spans = map.Columns[map.ColumnIndex(x, y)];
            spans.Clear();
            spans.Add(new VoxelSpan(0, zTop));
        }

        AddRockyOverhangs(map, noiseSeed);
    }

    private static float SampleHeight(MapData map, int x, int y, uint noiseSeed,
        Dictionary<GridCoord, CraterInfo> craters)
    {
        int mx = x / MetaSize;
        int my = y / MetaSize;

        var homeBiome = map.Biomes[map.MetaIndex(mx, my)];
        if (homeBiome == Biome.Crater &&
            craters.TryGetValue(new GridCoord(mx, my), out var ci))
        {
            float craterHeight = EvalCrater(x, y, ci);
            int localX = x - mx * MetaSize;
            int localY = y - my * MetaSize;
            int distFromEdge = Math.Min(Math.Min(localX, localY),
                Math.Min(MetaSize - 1 - localX, MetaSize - 1 - localY));
            if (distFromEdge < BlendRadius)
            {
                float blended = SampleBlendedNoise(map, x, y, noiseSeed, skipCraterCells: true);
                float t = distFromEdge / (float)BlendRadius;
                return Lerp(blended, craterHeight, t);
            }
            return craterHeight;
        }

        if (homeBiome == Biome.Start)
            return ProfileFor(Biome.Start).BaseHeight;

        if (WithinStartApron(map, x, y, out float apronRamp))
        {
            float startH = ProfileFor(Biome.Start).BaseHeight;
            float naturalH = SampleBlendedNoise(map, x, y, noiseSeed, skipCraterCells: false);
            return Lerp(startH, naturalH, apronRamp);
        }

        return SampleBlendedNoise(map, x, y, noiseSeed, skipCraterCells: false);
    }

    private static float SampleBlendedNoise(MapData map, int x, int y, uint noiseSeed,
        bool skipCraterCells)
    {
        float sum = 0f, weight = 0f;
        int mx = x / MetaSize, my = y / MetaSize;

        for (int dmy = -1; dmy <= 1; dmy++)
        for (int dmx = -1; dmx <= 1; dmx++)
        {
            int nmx = mx + dmx, nmy = my + dmy;
            if (nmx < 0 || nmy < 0 || nmx >= map.MetaWidth || nmy >= map.MetaHeight) continue;
            var b = map.Biomes[map.MetaIndex(nmx, nmy)];
            if (skipCraterCells && b == Biome.Crater) continue;
            if (b == Biome.Start || b == Biome.Crater) continue;

            float cx = (nmx + 0.5f) * MetaSize;
            float cy = (nmy + 0.5f) * MetaSize;
            float dist = MathF.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
            float w = MathF.Max(0f, 1f - dist / (MetaSize * 1.5f));
            if (w <= 0f) continue;

            var prof = ProfileFor(b);
            float n = Noise.Fbm(x, y, prof.Octaves, prof.Frequency, noiseSeed);
            float h = prof.BaseHeight + (n * 2f - 1f) * prof.Amplitude;
            sum += h * w;
            weight += w;
        }
        if (weight <= 0f) return 4f;
        return sum / weight;
    }

    private static bool WithinStartApron(MapData map, int x, int y, out float ramp)
    {
        ramp = 0f;
        if (!map.StartMeta.HasValue) return false;
        var sm = map.StartMeta.Value;
        int sxMin = sm.X * MetaSize;
        int syMin = sm.Y * MetaSize;
        int sxMax = sxMin + MetaSize - 1;
        int syMax = syMin + MetaSize - 1;
        if (x >= sxMin && x <= sxMax && y >= syMin && y <= syMax)
        {
            ramp = 0f;
            return true;
        }
        int dx = x < sxMin ? sxMin - x : (x > sxMax ? x - sxMax : 0);
        int dy = y < syMin ? syMin - y : (y > syMax ? y - syMax : 0);
        int dist = Math.Max(dx, dy);
        if (dist > StartApronWidth) return false;

        int mx = x / MetaSize, my = y / MetaSize;
        var b = map.Biomes[map.MetaIndex(mx, my)];
        if (b != Biome.Meadow && b != Biome.Forest) return false;

        ramp = dist / (float)StartApronWidth;
        return true;
    }

    // ---------- Crater ----------

    private readonly struct CraterInfo
    {
        public readonly float Cx, Cy;
        public readonly float Radius;
        public readonly float BaseH;
        public CraterInfo(float cx, float cy, float r, float b)
        { Cx = cx; Cy = cy; Radius = r; BaseH = b; }
    }

    private static Dictionary<GridCoord, CraterInfo> BuildCraterInfo(MapData map, ref Rng rng)
    {
        var result = new Dictionary<GridCoord, CraterInfo>();
        for (int my = 0; my < map.MetaHeight; my++)
        for (int mx = 0; mx < map.MetaWidth; mx++)
        {
            if (map.Biomes[map.MetaIndex(mx, my)] != Biome.Crater) continue;
            float cx = mx * MetaSize + rng.NextRange(2, 6);
            float cy = my * MetaSize + rng.NextRange(2, 6);
            float r = rng.NextRange(3, 7);
            result[new GridCoord(mx, my)] = new CraterInfo(cx, cy, r, 6f);
        }
        return result;
    }

    private static float EvalCrater(int x, int y, CraterInfo ci)
    {
        float dx = x - ci.Cx;
        float dy = y - ci.Cy;
        float d = MathF.Sqrt(dx * dx + dy * dy);
        if (d > ci.Radius) return ci.BaseH;
        float pitZone = 0.7f * ci.Radius;
        if (d >= pitZone)
        {
            float rimCenter = 0.85f * ci.Radius;
            float rimWidth = 0.15f * ci.Radius;
            float rimAmp = MathF.Max(0f, 1f - MathF.Abs(d - rimCenter) / rimWidth);
            return ci.BaseH + rimAmp * 2.5f;
        }
        float t = d / pitZone;
        return ci.BaseH - (1f - t) * 4f;
    }

    // ---------- Overhangs (Rocky only) ----------

    private static void AddRockyOverhangs(MapData map, uint noiseSeed)
    {
        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
        {
            int mx = x / MetaSize, my = y / MetaSize;
            if (map.Biomes[map.MetaIndex(mx, my)] != Biome.Rocky) continue;
            int h = map.Columns[map.ColumnIndex(x, y)][0].TopExclusive;

            int bestDelta = 0;
            (int nx, int ny) bestN = (x, y);
            foreach (var (nx, ny) in FourNeighbors(x, y, map.Width, map.Height))
            {
                int nh = map.Columns[map.ColumnIndex(nx, ny)][0].TopExclusive;
                int delta = nh - h;
                if (delta > bestDelta) { bestDelta = delta; bestN = (nx, ny); }
            }
            if (bestDelta < 4) continue;
            if (CellHashFloat(x, y, noiseSeed) < 0.7f) continue;  // 30% pass
            int overhangTop = map.Columns[map.ColumnIndex(bestN.nx, bestN.ny)][0].TopExclusive;
            int gap = 2;
            int thickness = 2;
            int slabBottom = overhangTop - thickness;
            if (slabBottom <= h + gap) continue;
            map.Columns[map.ColumnIndex(x, y)].Add(new VoxelSpan(slabBottom, thickness));
        }
    }

    private static IEnumerable<(int X, int Y)> FourNeighbors(int x, int y, int w, int h)
    {
        if (x > 0) yield return (x - 1, y);
        if (x + 1 < w) yield return (x + 1, y);
        if (y > 0) yield return (x, y - 1);
        if (y + 1 < h) yield return (x, y + 1);
    }

    private static float CellHashFloat(int x, int y, uint seed)
    {
        uint h = (uint)x * 0x9E3779B1u;
        h ^= (uint)y * 0x85EBCA6Bu;
        h ^= seed;
        h ^= h >> 16;
        h *= 0x7FEB352Du;
        h ^= h >> 15;
        return (h & 0x00FFFFFFu) / (float)(1 << 24);
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    // ---------- v1 minimal pass-2 ----------

    /// Sets every column NOT matching `skipPredicate` to a single solid span
    /// of given height. Used for v1's minimal pass-2 (everywhere outside the
    /// home base becomes a featureless plain).
    public static void FlatFill(MapData map, int height, Func<int, int, bool> skipPredicate)
    {
        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
        {
            if (skipPredicate(x, y)) continue;
            var spans = map.Columns[map.ColumnIndex(x, y)];
            spans.Clear();
            spans.Add(new VoxelSpan(0, height));
        }
    }
}

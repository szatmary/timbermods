using System.Collections.Generic;

namespace MapGen;

/// Wave Function Collapse solver for the biome metacell grid.
/// Tiled-WFC model: each cell starts with all 6 WFC biomes possible, we
/// observe the lowest-entropy cell (breaking ties with seeded RNG), propagate
/// constraints, repeat.
public static class BiomeGrid
{
    private const int MaxAttempts = 5;

    /// Returns the collapsed grid, or null if every attempt contradicted.
    /// The grid is row-major: grid[x + y*metaWidth].
    public static Biome[]? Solve(int metaWidth, int metaHeight, ref Rng rng)
    {
        for (int attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var result = TrySolve(metaWidth, metaHeight, ref rng);
            if (result != null) return result;
        }
        return null;
    }

    private static Biome[]? TrySolve(int metaWidth, int metaHeight, ref Rng rng)
    {
        int n = metaWidth * metaHeight;
        // domains[i] is a bitmask over WfcSet indices (bits 0..5).
        var domains = new int[n];
        int allMask = (1 << Biomes.WfcSet.Length) - 1;  // 0b00111111 = 63
        for (int i = 0; i < n; i++) domains[i] = allMask;

        int collapsedCount = 0;

        while (collapsedCount < n)
        {
            int idx = PickLowestEntropy(domains, ref rng);
            if (idx == -1) return null;   // contradiction somewhere
            if (idx == -2) break;         // all cells already collapsed

            int pick = ObservationPick(domains[idx], ref rng);
            domains[idx] = 1 << pick;
            collapsedCount++;

            if (!Propagate(domains, metaWidth, metaHeight, idx)) return null;

            // Propagation may have collapsed additional cells — count them.
            // We recount all singletons; slight over-count is harmless since
            // the loop exits on PickLowestEntropy returning -2.
            collapsedCount = 0;
            for (int i = 0; i < n; i++)
                if (PopCount(domains[i]) == 1) collapsedCount++;
        }

        // Build output: for each cell, read its singleton domain.
        var output = new Biome[n];
        for (int i = 0; i < n; i++)
        {
            int mask = domains[i];
            for (int b = 0; b < Biomes.WfcSet.Length; b++)
            {
                if ((mask & (1 << b)) != 0)
                {
                    output[i] = Biomes.WfcSet[b];
                    break;
                }
            }
        }
        return output;
    }

    private static int PickLowestEntropy(int[] domains, ref Rng rng)
    {
        int bestCount = int.MaxValue;
        var ties = new List<int>();
        for (int i = 0; i < domains.Length; i++)
        {
            int mask = domains[i];
            if (mask == 0) return -1;  // contradiction
            int count = PopCount(mask);
            if (count == 1) continue;  // already collapsed
            if (count < bestCount)
            {
                bestCount = count;
                ties.Clear();
                ties.Add(i);
            }
            else if (count == bestCount)
            {
                ties.Add(i);
            }
        }
        if (ties.Count == 0) return -2;  // no uncollapsed cells remain
        return ties[rng.NextRange(0, ties.Count)];
    }

    private static int ObservationPick(int mask, ref Rng rng)
    {
        // Weighted pick among remaining options using per-biome weights.
        var kept = new List<int>();
        var weights = new List<float>();
        for (int b = 0; b < Biomes.WfcSet.Length; b++)
        {
            if ((mask & (1 << b)) != 0)
            {
                kept.Add(b);
                weights.Add(Biomes.Weights[b]);
            }
        }
        int w = rng.WeightedPick(weights.ToArray());
        return kept[w];
    }

    private static bool Propagate(int[] domains, int metaWidth, int metaHeight, int start)
    {
        var stack = new Stack<int>();
        stack.Push(start);
        while (stack.Count > 0)
        {
            int i = stack.Pop();
            int x = i % metaWidth;
            int y = i / metaWidth;
            int mask = domains[i];

            foreach (var (nx, ny) in Neighbors(x, y, metaWidth, metaHeight))
            {
                int ni = nx + ny * metaWidth;
                int before = domains[ni];
                int after = before;

                // For each option nb in the neighbor's domain, check if ANY
                // option b in this cell's domain allows it. If none does, remove nb.
                for (int nb = 0; nb < Biomes.WfcSet.Length; nb++)
                {
                    if ((before & (1 << nb)) == 0) continue;
                    bool anyAllowed = false;
                    for (int b = 0; b < Biomes.WfcSet.Length; b++)
                    {
                        if ((mask & (1 << b)) == 0) continue;
                        if (Biomes.IsAllowedAdjacent(Biomes.WfcSet[b], Biomes.WfcSet[nb]))
                        {
                            anyAllowed = true;
                            break;
                        }
                    }
                    if (!anyAllowed) after &= ~(1 << nb);
                }

                if (after != before)
                {
                    if (after == 0) return false;  // contradiction
                    domains[ni] = after;
                    stack.Push(ni);
                }
            }
        }
        return true;
    }

    private static IEnumerable<(int X, int Y)> Neighbors(int x, int y, int w, int h)
    {
        if (x > 0)     yield return (x - 1, y);
        if (x + 1 < w) yield return (x + 1, y);
        if (y > 0)     yield return (x, y - 1);
        if (y + 1 < h) yield return (x, y + 1);
    }

    /// Rewrite crater cells on the grid edge to their most common neighbor biome.
    /// Spec section 1 post-WFC fix: craters may not appear on the map border.
    public static void RewriteEdgeCraters(Biome[] grid, int metaWidth, int metaHeight)
    {
        for (int y = 0; y < metaHeight; y++)
        for (int x = 0; x < metaWidth; x++)
        {
            bool onEdge = x == 0 || y == 0 || x == metaWidth - 1 || y == metaHeight - 1;
            if (!onEdge) continue;
            int i = x + y * metaWidth;
            if (grid[i] != Biome.Crater) continue;

            // Count non-crater neighbor biomes and pick the plurality.
            var counts = new int[7];
            foreach (var (nx, ny) in Neighbors(x, y, metaWidth, metaHeight))
            {
                var nb = grid[nx + ny * metaWidth];
                if (nb != Biome.Crater) counts[(byte)nb]++;
            }
            int bestB = 0, bestN = -1;
            for (int b = 0; b < counts.Length; b++)
            {
                if (counts[b] > bestN) { bestN = counts[b]; bestB = b; }
            }
            grid[i] = (Biome)bestB;
        }
    }

    /// Inline population count (Hamming weight) — netstandard2.1 doesn't have
    /// System.Numerics.BitOperations.PopCount.
    private static int PopCount(int mask)
    {
        uint x = (uint)mask;
        x -= (x >> 1) & 0x55555555u;
        x = (x & 0x33333333u) + ((x >> 2) & 0x33333333u);
        x = (x + (x >> 4)) & 0x0F0F0F0Fu;
        return (int)((x * 0x01010101u) >> 24);
    }
}

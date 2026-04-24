using System.Collections.Generic;

namespace MapGen;

/// Post-WFC Start labeling. Picks exactly one metacell and relabels it
/// with Biome.Start, based on the scoring table in spec section 5.
public static class StartSelection
{
    /// Returns the metacell coord picked as Start, or null if no candidate
    /// passed the disqualifier (shouldn't happen on a reasonable grid).
    /// The caller should then mutate grid[idx] = Biome.Start.
    public static GridCoord? Pick(Biome[] grid, int metaWidth, int metaHeight, ref Rng rng)
    {
        int bestScore = int.MinValue;
        var ties = new List<GridCoord>();

        for (int y = 0; y < metaHeight; y++)
        for (int x = 0; x < metaWidth; x++)
        {
            int score = ScoreCell(grid, metaWidth, metaHeight, x, y);
            if (score == int.MinValue) continue;  // disqualified
            if (score > bestScore)
            {
                bestScore = score;
                ties.Clear();
                ties.Add(new GridCoord(x, y));
            }
            else if (score == bestScore)
            {
                ties.Add(new GridCoord(x, y));
            }
        }
        if (ties.Count == 0) return null;
        return ties[rng.NextRange(0, ties.Count)];
    }

    /// Apply the pick — call after Pick returns non-null.
    public static void Apply(Biome[] grid, int metaWidth, GridCoord pick)
    {
        grid[pick.X + pick.Y * metaWidth] = Biome.Start;
    }

    private static int ScoreCell(Biome[] grid, int mw, int mh, int x, int y)
    {
        var here = grid[x + y * mw];
        int baseScore = here switch
        {
            Biome.Meadow => 3,
            Biome.Forest => 2,
            Biome.Badland => 0,
            _ => int.MinValue,  // Rocky / Sea / Crater / Start disqualified
        };
        if (baseScore == int.MinValue) return int.MinValue;

        int score = baseScore;
        bool onBorder = x == 0 || y == 0 || x == mw - 1 || y == mh - 1;
        if (onBorder) score -= 2;

        bool seaNeighborDirect = false;
        int meadowForestNeighbors = 0;
        foreach (var (nx, ny) in Neighbors(x, y, mw, mh))
        {
            var nb = grid[nx + ny * mw];
            if (nb == Biome.Sea) seaNeighborDirect = true;
            if (nb == Biome.Meadow || nb == Biome.Forest) meadowForestNeighbors++;
        }
        if (seaNeighborDirect) score += 3;
        else if (HasSeaWithin(grid, mw, mh, x, y, radius: 2)) score += 1;
        score += 2 * meadowForestNeighbors;

        return score;
    }

    private static bool HasSeaWithin(Biome[] grid, int mw, int mh, int cx, int cy, int radius)
    {
        for (int dy = -radius; dy <= radius; dy++)
        for (int dx = -radius; dx <= radius; dx++)
        {
            int nx = cx + dx, ny = cy + dy;
            if (nx < 0 || ny < 0 || nx >= mw || ny >= mh) continue;
            if (grid[nx + ny * mw] == Biome.Sea) return true;
        }
        return false;
    }

    private static IEnumerable<(int X, int Y)> Neighbors(int x, int y, int mw, int mh)
    {
        if (x > 0) yield return (x - 1, y);
        if (x + 1 < mw) yield return (x + 1, y);
        if (y > 0) yield return (x, y - 1);
        if (y + 1 < mh) yield return (x, y + 1);
    }
}

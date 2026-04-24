using System;
using System.Collections.Generic;

namespace MapGen;

/// Bridson's fast Poisson-disk sampling (2D). Returns integer coords
/// with minimum Euclidean distance `minDistance` between any pair.
public static class PoissonDisk
{
    public static List<GridCoord> Sample(int width, int height, float minDistance, ref Rng rng)
    {
        if (minDistance <= 0) throw new ArgumentOutOfRangeException(nameof(minDistance));

        float cellSize = minDistance / MathF.Sqrt(2f);
        int gw = (int)MathF.Ceiling(width / cellSize);
        int gh = (int)MathF.Ceiling(height / cellSize);
        var grid = new int[gw * gh];
        for (int i = 0; i < grid.Length; i++) grid[i] = -1;

        var points = new List<(float X, float Y)>();
        var active = new List<int>();

        float sx = width / 2f, sy = height / 2f;
        points.Add((sx, sy));
        active.Add(0);
        grid[(int)(sy / cellSize) * gw + (int)(sx / cellSize)] = 0;

        int maxAttempts = 30;
        while (active.Count > 0)
        {
            int activeIdx = rng.NextRange(0, active.Count);
            int pIdx = active[activeIdx];
            var (px, py) = points[pIdx];
            bool found = false;
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                float ang = rng.NextFloat() * MathF.PI * 2f;
                float r = minDistance * (1f + rng.NextFloat());
                float nx = px + r * MathF.Cos(ang);
                float ny = py + r * MathF.Sin(ang);
                if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;

                int gx = (int)(nx / cellSize);
                int gy = (int)(ny / cellSize);
                bool ok = true;
                for (int dy = -2; dy <= 2 && ok; dy++)
                for (int dx = -2; dx <= 2 && ok; dx++)
                {
                    int cx = gx + dx, cy = gy + dy;
                    if (cx < 0 || cy < 0 || cx >= gw || cy >= gh) continue;
                    int neighborIdx = grid[cy * gw + cx];
                    if (neighborIdx == -1) continue;
                    var (ox, oy) = points[neighborIdx];
                    float ddx = nx - ox, ddy = ny - oy;
                    if (ddx * ddx + ddy * ddy < minDistance * minDistance) ok = false;
                }
                if (ok)
                {
                    points.Add((nx, ny));
                    int newIdx = points.Count - 1;
                    grid[gy * gw + gx] = newIdx;
                    active.Add(newIdx);
                    found = true;
                    break;
                }
            }
            if (!found) active.RemoveAt(activeIdx);
        }

        var result = new List<GridCoord>(points.Count);
        foreach (var (x, y) in points) result.Add(new GridCoord((int)MathF.Round(x), (int)MathF.Round(y)));
        return result;
    }
}

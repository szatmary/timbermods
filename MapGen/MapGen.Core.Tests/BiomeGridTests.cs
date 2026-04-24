using Xunit;
using MapGen;

namespace MapGen.Tests;

public class BiomeGridTests
{
    [Fact]
    public void Solve_16x16_produces_fully_collapsed_grid()
    {
        var rng = new Rng(42u);
        var grid = BiomeGrid.Solve(metaWidth: 16, metaHeight: 16, ref rng);
        Assert.NotNull(grid);
        Assert.Equal(16 * 16, grid!.Length);
        foreach (var b in grid) Assert.Contains(b, Biomes.WfcSet);
    }

    [Fact]
    public void Solve_same_seed_same_grid()
    {
        var rng1 = new Rng(1u);
        var g1 = BiomeGrid.Solve(8, 8, ref rng1);
        var rng2 = new Rng(1u);
        var g2 = BiomeGrid.Solve(8, 8, ref rng2);
        Assert.NotNull(g1);
        Assert.NotNull(g2);
        Assert.Equal(g1, g2);
    }

    [Fact]
    public void Solve_all_adjacent_pairs_are_allowed()
    {
        var rng = new Rng(99u);
        var grid = BiomeGrid.Solve(16, 16, ref rng);
        Assert.NotNull(grid);
        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++)
        {
            var here = grid![x + y * 16];
            if (x + 1 < 16) Assert.True(Biomes.IsAllowedAdjacent(here, grid[(x + 1) + y * 16]));
            if (y + 1 < 16) Assert.True(Biomes.IsAllowedAdjacent(here, grid[x + (y + 1) * 16]));
        }
    }

    [Fact]
    public void Solve_over_many_seeds_no_contradiction_retries_exhaust()
    {
        int successes = 0;
        for (uint s = 0; s < 50; s++)
        {
            var rng = new Rng(s);
            if (BiomeGrid.Solve(16, 16, ref rng) != null) successes++;
        }
        Assert.True(successes >= 45, $"Only {successes}/50 seeds produced a grid — too many contradictions.");
    }
}

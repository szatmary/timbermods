using Xunit;
using MapGen;

namespace MapGen.Tests;

public class StartSelectionTests
{
    [Fact]
    public void Pick_never_selects_rocky_sea_or_crater()
    {
        for (uint s = 0; s < 30; s++)
        {
            var rng = new Rng(s);
            var grid = BiomeGrid.Solve(16, 16, ref rng)!;
            BiomeGrid.RewriteEdgeCraters(grid, 16, 16);
            var pick = StartSelection.Pick(grid, 16, 16, ref rng);
            Assert.NotNull(pick);
            int idx = pick!.Value.X + pick.Value.Y * 16;
            Assert.DoesNotContain(grid[idx], new[] { Biome.Rocky, Biome.Sea, Biome.Crater });
        }
    }

    [Fact]
    public void Pick_is_deterministic()
    {
        var r1 = new Rng(5u);
        var g1 = BiomeGrid.Solve(16, 16, ref r1)!;
        BiomeGrid.RewriteEdgeCraters(g1, 16, 16);
        var rngA = new Rng(5u);
        var aPick = StartSelection.Pick(g1, 16, 16, ref rngA);

        var r2 = new Rng(5u);
        var g2 = BiomeGrid.Solve(16, 16, ref r2)!;
        BiomeGrid.RewriteEdgeCraters(g2, 16, 16);
        var rngB = new Rng(5u);
        var bPick = StartSelection.Pick(g2, 16, 16, ref rngB);

        Assert.Equal(aPick, bPick);
    }
}

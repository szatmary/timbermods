using Xunit;
using MapGen;

namespace MapGen.Tests;

public class HomeBaseTests
{
    [Fact]
    public void LandUseGrid_starts_all_None()
    {
        var g = new LandUseGrid(24);
        for (int y = 0; y < 24; y++)
        for (int x = 0; x < 24; x++)
            Assert.Equal(LandUseGrid.Use.None, g.Get(x, y));
        Assert.Equal(24 * 24, g.CountWhere(LandUseGrid.Use.None));
    }

    [Fact]
    public void LandUseGrid_set_and_get_round_trip()
    {
        var g = new LandUseGrid(24);
        g.Set(10, 10, LandUseGrid.Use.DistrictCenter);
        Assert.Equal(LandUseGrid.Use.DistrictCenter, g.Get(10, 10));
        Assert.Equal(1, g.CountWhere(LandUseGrid.Use.DistrictCenter));
    }
}

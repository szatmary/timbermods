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

    [Fact]
    public void Generate_district_center_is_4x4()
    {
        var (map, catalog) = MakeFakeMapAndCatalog();
        var rng = new Rng("TEST1");
        var grid = HomeBase.Generate(map, catalog, 32, 32, hBase: 4,
            variant: WaterVariant.Pond, ref rng);
        Assert.Equal(16, grid.CountWhere(LandUseGrid.Use.DistrictCenter));
    }

    [Fact]
    public void Generate_district_ring_surrounds_center()
    {
        var (map, catalog) = MakeFakeMapAndCatalog();
        var rng = new Rng("TEST2");
        var grid = HomeBase.Generate(map, catalog, 32, 32, 4, WaterVariant.Pond, ref rng);
        // Footprint is 10x10 = 100 cells; center is 16. So ring should be
        // 100 - 16 = 84 cells.
        Assert.Equal(84, grid.CountWhere(LandUseGrid.Use.DistrictRing));
    }

    [Fact]
    public void Generate_farm_is_36_cells()
    {
        var (map, catalog) = MakeFakeMapAndCatalog();
        var rng = new Rng("TEST3");
        var grid = HomeBase.Generate(map, catalog, 32, 32, 4, WaterVariant.Pond, ref rng);
        // Farm should be exactly 6x6 = 36, OR 0 if the random search couldn't fit it.
        int farm = grid.CountWhere(LandUseGrid.Use.Farm);
        Assert.True(farm == 36 || farm == 0, $"Farm count {farm} should be 36 or 0.");
    }

    [Fact]
    public void Generate_pond_variant_has_at_least_16_water_cells()
    {
        var (map, catalog) = MakeFakeMapAndCatalog();
        var rng = new Rng("POND1");
        var grid = HomeBase.Generate(map, catalog, 32, 32, 4, WaterVariant.Pond, ref rng);
        int water = grid.CountWhere(LandUseGrid.Use.Water);
        Assert.True(water >= 16, $"Pond should have ≥16 water cells (the 4x4 heart), got {water}");
    }

    private static (MapData, Catalog) MakeFakeMapAndCatalog()
    {
        var map = new MapData(64, 64, "TEST");
        map.MetaWidth = 8; map.MetaHeight = 8;
        map.Biomes = new Biome[64];
        map.Columns = new System.Collections.Generic.List<VoxelSpan>[64 * 64];
        for (int i = 0; i < map.Columns.Length; i++)
            map.Columns[i] = new System.Collections.Generic.List<VoxelSpan>();
        map.WaterDepths = new byte[64 * 64];
        var catalog = new Catalog
        {
            Trees = new System.Collections.Generic.List<CatalogEntry> { new() { Key = "pine", BlueprintKey = "Pine", Weight = 1 } },
            Resources = new System.Collections.Generic.List<CatalogEntry> { new() { Key = "berries", BlueprintKey = "BlueberryBush", Weight = 1 } },
            Thorns = new System.Collections.Generic.List<CatalogEntry>(),
            Ruins = new System.Collections.Generic.List<RuinCatalogEntry>(),
            BlockObjects = new System.Collections.Generic.Dictionary<string, CatalogEntry>
            {
                { "start_marker", new() { Key = "start_marker", BlueprintKey = "StartingLocation", Weight = 1 } },
            },
        };
        return (map, catalog);
    }
}

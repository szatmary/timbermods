using Xunit;
using MapGen;

namespace MapGen.Tests;

public class AccessValidationTests
{
    [Fact]
    public void BFS_reaches_flat_cells_only()
    {
        var map = FlatMapWithCliff();
        var start = new VoxelCoord(8, 8, 5);
        var reach = AccessValidation.FloodFillReachable(map, start);
        Assert.Contains(new GridCoord(10, 10), reach.Cells);
        Assert.DoesNotContain(new GridCoord(20, 20), reach.Cells);
    }

    private static MapData FlatMapWithCliff()
    {
        var map = new MapData(32, 32, 1u);
        map.MetaWidth = 4; map.MetaHeight = 4;
        map.Biomes = new Biome[16];
        for (int i = 0; i < 16; i++) map.Biomes[i] = Biome.Meadow;
        map.Biomes[map.MetaIndex(1, 1)] = Biome.Start;
        map.StartMeta = new GridCoord(1, 1);
        map.Columns = new System.Collections.Generic.List<VoxelSpan>[32 * 32];
        for (int y = 0; y < 32; y++)
        for (int x = 0; x < 32; x++)
        {
            int h = (x >= 16 && y >= 16) ? 15 : 5;
            map.Columns[map.ColumnIndex(x, y)] =
                new System.Collections.Generic.List<VoxelSpan> { new VoxelSpan(0, h) };
        }
        map.WaterDepths = new byte[32 * 32];
        return map;
    }
}

using Xunit;
using MapGen;

namespace MapGen.Tests;

public class HeightmapTests
{
    [Fact]
    public void Build_produces_one_column_per_cell()
    {
        var map = MakeBareMap(64, 64, seedBiomes: Biome.Meadow);
        var rng = new Rng(1u);
        Heightmap.Build(map, ref rng);
        Assert.Equal(64 * 64, map.Columns.Length);
        foreach (var col in map.Columns) Assert.NotEmpty(col);
    }

    [Fact]
    public void Build_heights_within_biome_amplitude()
    {
        var map = MakeBareMap(32, 32, seedBiomes: Biome.Meadow);
        var rng = new Rng(1u);
        Heightmap.Build(map, ref rng);
        int minBase = 4 - 2;
        int maxBase = 4 + 2;
        for (int i = 0; i < map.Columns.Length; i++)
        {
            var top = map.Columns[i][0].TopExclusive - 1;
            Assert.InRange(top, minBase - 1, maxBase + 1);
        }
    }

    [Fact]
    public void Build_apron_around_start_is_flat()
    {
        var map = MakeBareMap(24, 24, seedBiomes: Biome.Meadow);
        var mstart = new GridCoord(1, 1);
        map.Biomes[map.MetaIndex(mstart)] = Biome.Start;
        map.StartMeta = mstart;
        var rng = new Rng(1u);
        Heightmap.Build(map, ref rng);

        int expected = 4;
        for (int vy = 8; vy < 16; vy++)
        for (int vx = 8; vx < 16; vx++)
        {
            var top = map.TopHeight(vx, vy);
            Assert.InRange(top, expected - 1, expected + 1);
        }
    }

    private static MapData MakeBareMap(int w, int h, Biome seedBiomes)
    {
        var map = new MapData(w, h, 1u);
        map.MetaWidth = w / 8;
        map.MetaHeight = h / 8;
        map.Biomes = new Biome[map.MetaWidth * map.MetaHeight];
        for (int i = 0; i < map.Biomes.Length; i++) map.Biomes[i] = seedBiomes;
        map.Columns = new System.Collections.Generic.List<VoxelSpan>[w * h];
        for (int i = 0; i < map.Columns.Length; i++)
            map.Columns[i] = new System.Collections.Generic.List<VoxelSpan>();
        map.WaterDepths = new byte[w * h];
        return map;
    }
}

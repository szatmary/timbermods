using Xunit;
using MapGen;

namespace MapGen.Tests;

public class HydrologyTests
{
    [Fact]
    public void Build_places_at_least_one_water_source()
    {
        var map = MakePreparedMap(64, 64, ref _seedRng);
        var rng = new Rng(1u);
        Hydrology.Build(map, ref rng);
        int sources = 0;
        foreach (var e in map.Entities)
            if (e.Kind == EntityKind.WaterSource || e.Kind == EntityKind.BadwaterSource)
                sources++;
        Assert.True(sources >= 1, "Expected at least one water source entity.");
    }

    [Fact]
    public void Build_carves_river_cells_reaching_map_edge_or_sea()
    {
        var map = MakePreparedMap(64, 64, ref _seedRng);
        var rng = new Rng(2u);
        Hydrology.Build(map, ref rng);
        bool reachesEdge = false;
        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
        {
            if (map.WaterDepths[map.ColumnIndex(x, y)] > 0 &&
                (x == 0 || y == 0 || x == map.Width - 1 || y == map.Height - 1))
            {
                reachesEdge = true; break;
            }
        }
        Assert.True(reachesEdge, "Expected river to reach a map edge drain.");
    }

    private Rng _seedRng = new Rng(100u);

    private static MapData MakePreparedMap(int w, int h, ref Rng seedRng)
    {
        var map = new MapData(w, h, 1u);
        map.MetaWidth = w / 8;
        map.MetaHeight = h / 8;
        map.Biomes = new Biome[map.MetaWidth * map.MetaHeight];
        for (int i = 0; i < map.Biomes.Length; i++)
            map.Biomes[i] = (i % 3 == 0) ? Biome.Forest : Biome.Meadow;
        var sm = new GridCoord(map.MetaWidth / 2, map.MetaHeight / 2);
        map.Biomes[map.MetaIndex(sm)] = Biome.Start;
        map.StartMeta = sm;
        map.Columns = new System.Collections.Generic.List<VoxelSpan>[w * h];
        for (int i = 0; i < map.Columns.Length; i++)
            map.Columns[i] = new System.Collections.Generic.List<VoxelSpan>();
        map.WaterDepths = new byte[w * h];
        var rng2 = new Rng(seedRng.NextUInt());
        Heightmap.Build(map, ref rng2);
        return map;
    }
}

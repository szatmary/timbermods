using Xunit;
using MapGen;

namespace MapGen.Tests;

public class PrimitivesTests
{
    [Fact]
    public void Rng_same_seed_produces_same_sequence()
    {
        var a = new Rng(42u);
        var b = new Rng(42u);
        for (int i = 0; i < 10; i++)
            Assert.Equal(a.NextUInt(), b.NextUInt());
    }

    [Fact]
    public void Rng_next_float_in_zero_to_one()
    {
        var r = new Rng(1u);
        for (int i = 0; i < 1000; i++)
        {
            var f = r.NextFloat();
            Assert.InRange(f, 0f, 1f);
        }
    }

    [Fact]
    public void Rng_range_returns_value_in_half_open_interval()
    {
        var r = new Rng(7u);
        for (int i = 0; i < 1000; i++)
        {
            var v = r.NextRange(5, 10);
            Assert.InRange(v, 5, 9);
        }
    }

    [Fact]
    public void GridCoord_equality_and_hash_are_consistent()
    {
        var a = new GridCoord(3, 4);
        var b = new GridCoord(3, 4);
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void VoxelSpan_with_valid_bounds_computes_height()
    {
        var s = new VoxelSpan(0, 5);
        Assert.Equal(5, s.Height);
    }
}

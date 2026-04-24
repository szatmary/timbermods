using Xunit;
using MapGen;

namespace MapGen.Tests;

public class NoiseTests
{
    [Fact]
    public void Noise_deterministic_for_same_seed()
    {
        var a = Noise.Sample(1.5f, 2.5f, seed: 42u);
        var b = Noise.Sample(1.5f, 2.5f, seed: 42u);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Noise_in_zero_to_one_range()
    {
        for (int i = 0; i < 1000; i++)
        {
            float x = i * 0.13f;
            float y = i * 0.27f;
            var v = Noise.Sample(x, y, seed: 99u);
            Assert.InRange(v, 0f, 1f);
        }
    }

    [Fact]
    public void Fbm_deterministic()
    {
        var a = Noise.Fbm(0.5f, 0.5f, octaves: 4, frequency: 0.1f, seed: 7u);
        var b = Noise.Fbm(0.5f, 0.5f, octaves: 4, frequency: 0.1f, seed: 7u);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Fbm_varies_with_position()
    {
        var a = Noise.Fbm(0f, 0f, 4, 0.1f, 1u);
        var b = Noise.Fbm(10f, 10f, 4, 0.1f, 1u);
        Assert.NotEqual(a, b);
    }
}

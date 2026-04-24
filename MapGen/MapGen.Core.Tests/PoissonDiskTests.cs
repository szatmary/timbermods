using Xunit;
using MapGen;

namespace MapGen.Tests;

public class PoissonDiskTests
{
    [Fact]
    public void Sample_respects_min_spacing()
    {
        var rng = new Rng(7u);
        var samples = PoissonDisk.Sample(width: 50, height: 50, minDistance: 5f, ref rng);
        for (int i = 0; i < samples.Count; i++)
        for (int j = i + 1; j < samples.Count; j++)
        {
            int dx = samples[i].X - samples[j].X;
            int dy = samples[i].Y - samples[j].Y;
            float d = System.MathF.Sqrt(dx * dx + dy * dy);
            // Allow ~1.0 tolerance for rounding error: float coords that are minDistance apart
            // can round to integers that are closer due to rounding both coordinates down/toward each other.
            Assert.True(d >= 5f - 1.0f, $"Points {samples[i]} and {samples[j]} too close: {d}");
        }
    }

    [Fact]
    public void Sample_is_deterministic()
    {
        var r1 = new Rng(1u);
        var s1 = PoissonDisk.Sample(30, 30, 3f, ref r1);
        var r2 = new Rng(1u);
        var s2 = PoissonDisk.Sample(30, 30, 3f, ref r2);
        Assert.Equal(s1, s2);
    }

    [Fact]
    public void Sample_produces_nontrivial_count()
    {
        var rng = new Rng(2u);
        var samples = PoissonDisk.Sample(100, 100, 5f, ref rng);
        Assert.True(samples.Count > 20, $"Expected >20 samples, got {samples.Count}");
    }
}

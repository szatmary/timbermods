using System;

namespace MapGen;

/// Deterministic value-noise + fBM. Pure function of (x, y, seed) — no
/// hidden state, no .NET Random. Used by Heightmap for per-biome noise.
public static class Noise
{
    /// Single-octave value-noise sample at (x, y). Returns value in [0, 1].
    public static float Sample(float x, float y, uint seed)
    {
        int xi = Floor(x);
        int yi = Floor(y);
        float xf = x - xi;
        float yf = y - yi;
        float u = Smoothstep(xf);
        float v = Smoothstep(yf);
        float n00 = Hash01(xi,     yi,     seed);
        float n10 = Hash01(xi + 1, yi,     seed);
        float n01 = Hash01(xi,     yi + 1, seed);
        float n11 = Hash01(xi + 1, yi + 1, seed);
        float nx0 = Lerp(n00, n10, u);
        float nx1 = Lerp(n01, n11, u);
        return Lerp(nx0, nx1, v);
    }

    /// Fractional-Brownian-Motion: stack of octaves with doubling freq
    /// and halving amplitude per octave. Returns value in [0, 1].
    public static float Fbm(float x, float y, int octaves, float frequency, uint seed)
    {
        float sum = 0f;
        float amplitude = 1f;
        float totalAmp = 0f;
        float freq = frequency;
        for (int i = 0; i < octaves; i++)
        {
            sum += amplitude * Sample(x * freq, y * freq, seed + (uint)i);
            totalAmp += amplitude;
            amplitude *= 0.5f;
            freq *= 2f;
        }
        return sum / totalAmp;
    }

    private static int Floor(float v) => (int)Math.Floor(v);
    private static float Smoothstep(float t) => t * t * (3f - 2f * t);
    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    /// Hash (xi, yi, seed) → float in [0, 1]. Same inputs → same output.
    private static float Hash01(int xi, int yi, uint seed)
    {
        uint h = (uint)xi * 0x9E3779B1u;
        h ^= (uint)yi * 0x85EBCA6Bu;
        h ^= seed * 0xC2B2AE35u;
        h ^= h >> 16;
        h *= 0x7FEB352Du;
        h ^= h >> 15;
        h *= 0x846CA68Bu;
        h ^= h >> 16;
        return (h & 0x00FFFFFFu) / (float)(1 << 24);
    }
}

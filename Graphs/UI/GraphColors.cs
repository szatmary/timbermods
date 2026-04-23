using UnityEngine;

namespace Graphs.UI;

/// Deterministic color palette keyed on a metric id. Uses a hash of the id
/// to pick a hue, then fixes S and V per-category to keep related metrics
/// visually coherent.
public static class GraphColors
{
    public static Color ColorFor(string metricId, Graphs.Metrics.MetricCategory category)
    {
        float hue = (Hash(metricId) % 360u) / 360f;

        (float sat, float val) = category switch
        {
            Graphs.Metrics.MetricCategory.Goods      => (0.70f, 0.95f),
            Graphs.Metrics.MetricCategory.Population => (0.80f, 0.85f),
            Graphs.Metrics.MetricCategory.Science    => (0.55f, 0.95f),
            Graphs.Metrics.MetricCategory.Wellbeing  => (0.60f, 0.80f),
            _                                        => (0.60f, 0.90f),
        };

        return Color.HSVToRGB(hue, sat, val);
    }

    private static uint Hash(string s)
    {
        // Deterministic FNV-1a across processes/runs; Unity colors from
        // the same id always match.
        const uint fnvOffset = 2166136261u;
        const uint fnvPrime = 16777619u;
        uint h = fnvOffset;
        foreach (char c in s)
        {
            h ^= c;
            h *= fnvPrime;
        }
        return h;
    }
}

using LogBook.Metrics;
using UnityEngine;

namespace LogBook.UI;

/// Deterministic palette inspired by Timberborn's own UI: warm woody browns,
/// muted forest greens, pond blues, and soft reds. Line color is stable per
/// metric id so the legend swatch and the chart line always match.
public static class GraphColors
{
    /// Tint applied to the wellbeing-category legend / gutter icons (warm
    /// gold) so the wellbeing / hunger / thirst trio reads as one group.
    public static readonly Color WellbeingTint = new(0.96f, 0.80f, 0.48f);

    private static readonly Color[] Palette =
    {
        new(0.92f, 0.72f, 0.39f), // warm wood
        new(0.78f, 0.54f, 0.34f), // timber
        new(0.56f, 0.40f, 0.28f), // dark wood
        new(0.85f, 0.62f, 0.30f), // amber
        new(0.95f, 0.45f, 0.25f), // roasted orange
        new(0.86f, 0.36f, 0.30f), // muted red
        new(0.72f, 0.28f, 0.32f), // cranberry
        new(0.90f, 0.60f, 0.68f), // soft pink
        new(0.78f, 0.44f, 0.68f), // mauve
        new(0.56f, 0.42f, 0.78f), // plum
        new(0.42f, 0.44f, 0.80f), // periwinkle
        new(0.32f, 0.52f, 0.78f), // dusty blue
        new(0.30f, 0.66f, 0.82f), // pond
        new(0.40f, 0.78f, 0.78f), // teal
        new(0.36f, 0.72f, 0.54f), // reed green
        new(0.52f, 0.74f, 0.36f), // grass
        new(0.68f, 0.78f, 0.34f), // lichen
        new(0.82f, 0.80f, 0.32f), // straw
        new(0.95f, 0.85f, 0.40f), // golden
        new(0.68f, 0.58f, 0.32f), // ochre
        new(0.52f, 0.50f, 0.36f), // moss
        new(0.70f, 0.70f, 0.74f), // stone
        new(0.58f, 0.60f, 0.68f), // slate
        new(0.90f, 0.80f, 0.66f), // parchment
    };

    public static Color ColorFor(string metricId, MetricCategory category)
    {
        uint h = Hash(metricId);
        var baseColor = Palette[h % (uint)Palette.Length];

        // Slight HSV tweak per category so categories feel coherent without
        // being monochrome.
        return category switch
        {
            MetricCategory.Population => Shift(baseColor, 0f, 1.10f, 1.00f),
            MetricCategory.Science    => Shift(baseColor, 0f, 0.80f, 1.10f),
            MetricCategory.Wellbeing  => Shift(baseColor, 0f, 0.90f, 1.05f),
            _                         => baseColor,
        };
    }

    private static Color Shift(Color c, float hueDelta, float satScale, float valScale)
    {
        Color.RGBToHSV(c, out float hh, out float ss, out float vv);
        hh = (hh + hueDelta + 1f) % 1f;
        ss = Mathf.Clamp01(ss * satScale);
        vv = Mathf.Clamp01(vv * valScale);
        return Color.HSVToRGB(hh, ss, vv);
    }

    private static uint Hash(string s)
    {
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

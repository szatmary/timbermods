using Graphs.Metrics;
using UnityEngine;
using UnityEngine.UIElements;

namespace Graphs.UI;

public sealed class GraphsChart
{
    private static readonly Color DroughtColor   = new(1.00f, 0.55f, 0.15f, 0.15f);
    private static readonly Color BadtideColor   = new(0.60f, 0.25f, 0.80f, 0.15f);
    private static readonly Color TemperateColor = new(0, 0, 0, 0);

    private readonly MetricSampler _sampler;
    private readonly GraphsRangeSelector _range;
    private readonly MetricRegistry _registry;
    private readonly GraphsLegend _legend;

    private VisualElement? _element;

    public GraphsChart(
        MetricSampler sampler,
        GraphsRangeSelector range,
        MetricRegistry registry,
        GraphsLegend legend)
    {
        _sampler = sampler;
        _range = range;
        _registry = registry;
        _legend = legend;
    }

    public VisualElement Build()
    {
        _element = new VisualElement { name = "graphs-chart" };
        _element.style.flexGrow = 1;
        _element.style.backgroundColor = new StyleColor(new Color(0.04f, 0.04f, 0.06f));
        _element.generateVisualContent += Draw;
        _range.Changed += () => _element?.MarkDirtyRepaint();
        _legend.Changed += () => _element?.MarkDirtyRepaint();
        _sampler.OnSampled += () => _element?.MarkDirtyRepaint();
        return _element;
    }

    public void Repaint() => _element?.MarkDirtyRepaint();

    private void Draw(MeshGenerationContext ctx)
    {
        var rect = ctx.visualElement.contentRect;
        if (rect.width <= 0 || rect.height <= 0) return;

        var history = _sampler.History;
        if (history.Count == 0) return;

        float latestTimestamp = history.ReadTimestamp(history.Count - 1);
        float earliestTimestamp = history.ReadTimestamp(0);
        float? lookback = _range.LookbackDays();
        // When the buffer holds less data than the requested lookback, auto-zoom
        // to what we have instead of cramming it at the right edge.
        float startTimestamp = lookback.HasValue
            ? System.Math.Max(latestTimestamp - lookback.Value, earliestTimestamp)
            : earliestTimestamp;

        int startIdx = history.FindFirstAtOrAfter(startTimestamp);
        int endIdx = history.Count;
        if (startIdx >= endIdx) return;

        DrawWeatherBands(ctx, rect, history, startIdx, endIdx, startTimestamp, latestTimestamp);
        DrawGridlines(ctx, rect);
        DrawLines(ctx, rect, history, startIdx, endIdx, startTimestamp, latestTimestamp);
    }

    private static void DrawGridlines(MeshGenerationContext ctx, Rect rect)
    {
        var color = new Color(0.25f, 0.25f, 0.28f, 0.5f);
        for (int i = 1; i <= 4; i++)
        {
            float y = rect.y + rect.height * i / 5f;
            FillRect(ctx, new Rect(rect.x, y, rect.width, 1), color);
        }
    }

    // Log each distinct metric id ONCE per session when we first draw it,
    // so we can see actual min/max/NaN counts when the user toggles it on.
    private readonly HashSet<string> _loggedMetrics = new();

    private void DrawLines(
        MeshGenerationContext ctx, Rect rect, MetricHistory history,
        int startIdx, int endIdx, float startT, float endT)
    {
        float span = endT - startT;
        if (span <= 0) return;
        int sampleCount = endIdx - startIdx;
        if (sampleCount < 1) return;

        // Pass 1: compute per-category min/max across every visible metric
        // in the category, so all lines in one category share a y-scale.
        var categoryRanges = new Dictionary<MetricCategory, (float Min, float Max)>();
        var metrics = _registry.Metrics;
        for (int m = 0; m < metrics.Count; m++)
        {
            var def = metrics[m];
            if (!_legend.VisibleMetricIds.Contains(def.Id)) continue;

            categoryRanges.TryGetValue(def.Category, out var cur);
            if (cur.Min == 0 && cur.Max == 0 && !categoryRanges.ContainsKey(def.Category))
                cur = (float.PositiveInfinity, float.NegativeInfinity);

            for (int i = startIdx; i < endIdx; i++)
            {
                float v = history.ReadValue(i, m);
                if (float.IsNaN(v)) continue;
                if (v < cur.Min) cur = (v, cur.Max);
                if (v > cur.Max) cur = (cur.Min, v);
            }
            categoryRanges[def.Category] = cur;
        }

        // Inset the y-axis slightly so lines at min/max values aren't
        // clipped against the chart edges.
        const float yMargin = 6f;
        float innerTop = rect.y + yMargin;
        float innerBottom = rect.y + rect.height - yMargin;
        float innerHeight = innerBottom - innerTop;

        // Pass 2: draw each visible metric using its category's shared scale.
        for (int m = 0; m < metrics.Count; m++)
        {
            var def = metrics[m];
            if (!_legend.VisibleMetricIds.Contains(def.Id)) continue;
            if (!categoryRanges.TryGetValue(def.Category, out var catRange)) continue;
            if (float.IsInfinity(catRange.Min) || float.IsInfinity(catRange.Max)) continue;

            float range = catRange.Max - catRange.Min;
            Color color = GraphColors.ColorFor(def.Id, def.Category);

            if (_loggedMetrics.Add(def.Id))
            {
                int nanCount = 0;
                for (int i = startIdx; i < endIdx; i++)
                    if (float.IsNaN(history.ReadValue(i, m))) nanCount++;
                Debug.Log($"[Graphs] drawing '{def.Id}': category={def.Category} catMin={catRange.Min} catMax={catRange.Max} nan={nanCount}/{sampleCount}");
            }

            bool havePrev = false;
            Vector2 prev = default;
            for (int i = startIdx; i < endIdx; i++)
            {
                float v = history.ReadValue(i, m);
                if (float.IsNaN(v)) { havePrev = false; continue; }
                float t = history.ReadTimestamp(i);
                float x = rect.x + ((t - startT) / span) * rect.width;
                float norm = range > 0 ? (v - catRange.Min) / range : 0.5f;
                float y = innerBottom - norm * innerHeight;

                FillRect(ctx, new Rect(x - 2, y - 2, 4, 4), color);
                if (havePrev)
                    DrawSegment(ctx, prev, new Vector2(x, y), color, thickness: 3f);
                prev = new Vector2(x, y);
                havePrev = true;
            }
        }
    }

    /// Draw a line segment as a chain of overlapping axis-aligned rects.
    /// The rotated-quad mesh approach via `ctx.Allocate(4, 6)` was unreliable
    /// in Timberborn's UIToolkit pipeline — FillRect with axis-aligned geometry
    /// always renders, so we subdivide the segment and paint small stamps.
    private static void DrawSegment(
        MeshGenerationContext ctx, Vector2 a, Vector2 b, Color color, float thickness)
    {
        Vector2 delta = b - a;
        float len = delta.magnitude;
        if (len < 0.0001f) return;

        // Stamp every ~1.5 pixels along the path — dense enough that a 3px
        // stamp always overlaps with neighbors even on diagonal lines.
        float step = 1.5f;
        int steps = (int)(len / step);
        if (steps < 1) steps = 1;
        float half = thickness * 0.5f;

        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            float x = a.x + delta.x * t;
            float y = a.y + delta.y * t;
            FillRect(ctx, new Rect(x - half, y - half, thickness, thickness), color);
        }
    }

    private static void DrawWeatherBands(
        MeshGenerationContext ctx, Rect rect, MetricHistory history,
        int startIdx, int endIdx, float startT, float endT)
    {
        float span = endT - startT;
        if (span <= 0) return;

        int? runStart = null;
        byte runWeather = MetricHistory.WeatherTemperate;

        for (int i = startIdx; i <= endIdx; i++)
        {
            byte w = i < endIdx ? history.ReadWeather(i) : (byte)255;
            if (!runStart.HasValue) { runStart = i; runWeather = w; continue; }
            if (w == runWeather && i < endIdx) continue;

            if (runWeather != MetricHistory.WeatherTemperate)
            {
                float t0 = history.ReadTimestamp(runStart.Value);
                float t1 = i < endIdx ? history.ReadTimestamp(i) : endT;
                float x0 = rect.x + ((t0 - startT) / span) * rect.width;
                float x1 = rect.x + ((t1 - startT) / span) * rect.width;
                var color = runWeather switch
                {
                    MetricHistory.WeatherDrought => DroughtColor,
                    MetricHistory.WeatherBadtide => BadtideColor,
                    _                            => TemperateColor,
                };
                FillRect(ctx, new Rect(x0, rect.y, x1 - x0, rect.height), color);
            }
            runStart = i;
            runWeather = w;
        }
    }

    private static void FillRect(MeshGenerationContext ctx, Rect rect, Color color)
    {
        if (color.a <= 0 || rect.width <= 0 || rect.height <= 0) return;

        var mesh = ctx.Allocate(4, 6);
        mesh.SetNextVertex(new Vertex { position = new Vector3(rect.xMin, rect.yMin, Vertex.nearZ), tint = color });
        mesh.SetNextVertex(new Vertex { position = new Vector3(rect.xMax, rect.yMin, Vertex.nearZ), tint = color });
        mesh.SetNextVertex(new Vertex { position = new Vector3(rect.xMax, rect.yMax, Vertex.nearZ), tint = color });
        mesh.SetNextVertex(new Vertex { position = new Vector3(rect.xMin, rect.yMax, Vertex.nearZ), tint = color });
        mesh.SetNextIndex(0); mesh.SetNextIndex(1); mesh.SetNextIndex(2);
        mesh.SetNextIndex(0); mesh.SetNextIndex(2); mesh.SetNextIndex(3);
    }
}

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

    private void DrawLines(
        MeshGenerationContext ctx, Rect rect, MetricHistory history,
        int startIdx, int endIdx, float startT, float endT)
    {
        float span = endT - startT;
        if (span <= 0) return;
        int sampleCount = endIdx - startIdx;
        if (sampleCount < 1) return;

        var metrics = _registry.Metrics;
        for (int m = 0; m < metrics.Count; m++)
        {
            var def = metrics[m];
            if (!_legend.VisibleMetricIds.Contains(def.Id)) continue;

            float min = float.PositiveInfinity, max = float.NegativeInfinity;
            for (int i = startIdx; i < endIdx; i++)
            {
                float v = history.ReadValue(i, m);
                if (float.IsNaN(v)) continue;
                if (v < min) min = v;
                if (v > max) max = v;
            }
            if (float.IsInfinity(min) || float.IsInfinity(max)) continue;

            float range = max - min;
            Color color = GraphColors.ColorFor(def.Id, def.Category);

            bool havePrev = false;
            Vector2 prev = default;
            for (int i = startIdx; i < endIdx; i++)
            {
                float v = history.ReadValue(i, m);
                if (float.IsNaN(v)) { havePrev = false; continue; }
                float t = history.ReadTimestamp(i);
                // When span is 0 (single sample) pin to right edge.
                float x = span > 0 ? rect.x + ((t - startT) / span) * rect.width : rect.xMax - 4;
                float norm = range > 0 ? (v - min) / range : 0.5f;
                float y = rect.y + rect.height - norm * rect.height;

                if (havePrev)
                    DrawSegment(ctx, prev, new Vector2(x, y), color, thickness: 2f);
                else
                    // Draw a small dot at the first point so a single-sample metric
                    // is still visible (otherwise a fresh game shows a blank chart
                    // until the second hourly sample arrives).
                    FillRect(ctx, new Rect(x - 2, y - 2, 4, 4), color);
                prev = new Vector2(x, y);
                havePrev = true;
            }
        }
    }

    private static void DrawSegment(
        MeshGenerationContext ctx, Vector2 a, Vector2 b, Color color, float thickness)
    {
        Vector2 dir = b - a;
        float len = dir.magnitude;
        if (len < 0.0001f) return;
        Vector2 normal = new Vector2(-dir.y, dir.x) / len * thickness * 0.5f;

        Vector3 v0 = new(a.x + normal.x, a.y + normal.y, Vertex.nearZ);
        Vector3 v1 = new(b.x + normal.x, b.y + normal.y, Vertex.nearZ);
        Vector3 v2 = new(b.x - normal.x, b.y - normal.y, Vertex.nearZ);
        Vector3 v3 = new(a.x - normal.x, a.y - normal.y, Vertex.nearZ);

        var mesh = ctx.Allocate(4, 6);
        mesh.SetNextVertex(new Vertex { position = v0, tint = color });
        mesh.SetNextVertex(new Vertex { position = v1, tint = color });
        mesh.SetNextVertex(new Vertex { position = v2, tint = color });
        mesh.SetNextVertex(new Vertex { position = v3, tint = color });
        mesh.SetNextIndex(0); mesh.SetNextIndex(1); mesh.SetNextIndex(2);
        mesh.SetNextIndex(0); mesh.SetNextIndex(2); mesh.SetNextIndex(3);
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

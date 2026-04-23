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

    private VisualElement? _element;

    public GraphsChart(MetricSampler sampler, GraphsRangeSelector range)
    {
        _sampler = sampler;
        _range = range;
    }

    public VisualElement Build()
    {
        _element = new VisualElement { name = "graphs-chart" };
        _element.style.flexGrow = 1;
        _element.style.backgroundColor = new StyleColor(new Color(0.04f, 0.04f, 0.06f));
        _element.generateVisualContent += Draw;
        _range.Changed += () => _element?.MarkDirtyRepaint();
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
        float? lookback = _range.LookbackDays();
        float startTimestamp = lookback.HasValue
            ? latestTimestamp - lookback.Value
            : history.ReadTimestamp(0);

        int startIdx = history.FindFirstAtOrAfter(startTimestamp);
        int endIdx = history.Count;
        if (startIdx >= endIdx) return;

        DrawWeatherBands(ctx, rect, history, startIdx, endIdx, startTimestamp, latestTimestamp);
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

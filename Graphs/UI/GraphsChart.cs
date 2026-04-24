using Graphs.Metrics;
using UnityEngine;
using UnityEngine.UIElements;

namespace Graphs.UI;

public sealed class GraphsChart
{
    // Tints chosen to match the game's own weather theming:
    // - Drought: hazy amber/yellow (like the dry-season skies and grass)
    // - Badtide: sickly pink-magenta (like badwater)
    private static readonly Color DroughtColor   = new(0.95f, 0.70f, 0.20f, 0.18f);
    private static readonly Color BadtideColor   = new(0.90f, 0.30f, 0.55f, 0.20f);
    private static readonly Color TemperateColor = new(0, 0, 0, 0);

    private readonly MetricSampler _sampler;
    private readonly GraphsRangeSelector _range;
    private readonly MetricRegistry _registry;
    private readonly GraphsLegend _legend;

    private VisualElement? _element;

    // Cursor-tracking state for the hover tooltip.
    private Vector2 _cursorLocal;
    private bool _cursorInside;
    private VisualElement? _cursorLine;
    private VisualElement? _tooltipBox;
    private Label? _tooltipHeader;
    private readonly System.Collections.Generic.List<Label> _tooltipRows = new();

    // Per-metric icon + value label that sit in the gutter on the right edge
    // of the chart next to the latest sample's y. Built lazily on first
    // update; positioned on every repaint trigger.
    private const float EndIconSize = 18f;
    private const float GutterWidth = EndIconSize + 6f; // icon + padding
    private readonly System.Collections.Generic.Dictionary<string, Image> _endIcons = new();
    private readonly System.Collections.Generic.Dictionary<string, Label> _endLabels = new();

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
        _range.Changed += OnRepaintTrigger;
        _legend.Changed += OnRepaintTrigger;
        _sampler.OnSampled += OnRepaintTrigger;
        _element.RegisterCallback<GeometryChangedEvent>(_ => UpdateEndIcons());

        // Hover tracking: a vertical cursor line + a floating tooltip panel.
        _element.RegisterCallback<MouseMoveEvent>(e =>
        {
            _cursorLocal = (Vector2)e.localMousePosition;
            _cursorInside = true;
            RefreshTooltip();
        });
        _element.RegisterCallback<MouseLeaveEvent>(_ =>
        {
            _cursorInside = false;
            HideTooltip();
        });

        _cursorLine = new VisualElement { name = "graphs-chart-cursor", pickingMode = PickingMode.Ignore };
        _cursorLine.style.position = Position.Absolute;
        _cursorLine.style.top = 0;
        _cursorLine.style.bottom = 0;
        _cursorLine.style.width = 1;
        _cursorLine.style.backgroundColor = new StyleColor(new Color(1f, 1f, 1f, 0.35f));
        _cursorLine.style.display = DisplayStyle.None;
        _element.Add(_cursorLine);

        _tooltipBox = new VisualElement { name = "graphs-chart-tooltip", pickingMode = PickingMode.Ignore };
        _tooltipBox.style.position = Position.Absolute;
        _tooltipBox.style.display = DisplayStyle.None;
        _tooltipBox.style.backgroundColor = new StyleColor(new Color(0.08f, 0.07f, 0.06f, 0.94f));
        _tooltipBox.style.borderTopLeftRadius = 4;
        _tooltipBox.style.borderTopRightRadius = 4;
        _tooltipBox.style.borderBottomLeftRadius = 4;
        _tooltipBox.style.borderBottomRightRadius = 4;
        _tooltipBox.style.paddingTop = 6;
        _tooltipBox.style.paddingBottom = 6;
        _tooltipBox.style.paddingLeft = 8;
        _tooltipBox.style.paddingRight = 8;
        _tooltipBox.style.minWidth = 160;

        _tooltipHeader = new Label();
        _tooltipHeader.style.color = new Color(0.96f, 0.86f, 0.62f);
        _tooltipHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
        _tooltipHeader.style.marginBottom = 4;
        _tooltipBox.Add(_tooltipHeader);

        _element.Add(_tooltipBox);

        return _element;
    }

    public void Repaint()
    {
        _element?.MarkDirtyRepaint();
        UpdateEndIcons();
    }

    private void OnRepaintTrigger()
    {
        _element?.MarkDirtyRepaint();
        UpdateEndIcons();
    }

    /// For every visible metric with a last-sample position, place its icon
    /// at the right edge of the chart at the line's final y. Hides icons of
    /// metrics that aren't visible (or don't have an icon).
    private void UpdateEndIcons()
    {
        if (_element is null) return;

        var rect = _element.contentRect;
        if (rect.width <= 0 || rect.height <= 0) { HideAllEndIcons(); return; }
        var draw = DrawArea(rect);

        var history = _sampler.History;
        if (history.Count == 0) { HideAllEndIcons(); return; }

        float latestT = history.ReadTimestamp(history.Count - 1);
        float earliestT = history.ReadTimestamp(0);
        float? lookback = _range.LookbackDays();
        float startT = lookback.HasValue
            ? System.Math.Max(latestT - lookback.Value, earliestT)
            : earliestT;
        int startIdx = history.FindFirstAtOrAfter(startT);
        int endIdx = history.Count;
        if (startIdx >= endIdx) { HideAllEndIcons(); return; }

        // Recompute per-ScaleGroup max to match Draw(). Keep in sync.
        var scaleMax = new System.Collections.Generic.Dictionary<string, float>(System.StringComparer.Ordinal);
        var metrics = _registry.Metrics;
        for (int m = 0; m < metrics.Count; m++)
        {
            var def = metrics[m];
            if (!_legend.VisibleMetricIds.Contains(def.Id)) continue;
            scaleMax.TryGetValue(def.ScaleGroup, out var cur);
            for (int i = startIdx; i < endIdx; i++)
            {
                float v = history.ReadValue(i, m);
                if (float.IsNaN(v)) continue;
                if (v > cur) cur = v;
            }
            scaleMax[def.ScaleGroup] = cur;
        }

        const float topInset = 6f;
        float innerTop = draw.y + topInset;
        float innerBottom = draw.y + draw.height;
        float innerHeight = innerBottom - innerTop;

        // Gather candidates with their preferred y anchored to the line end.
        var candidates = new System.Collections.Generic.List<(MetricDefinition Def, float Y, Sprite Sprite)>();
        int last = endIdx - 1;

        for (int m = 0; m < metrics.Count; m++)
        {
            var def = metrics[m];
            if (!_legend.VisibleMetricIds.Contains(def.Id)) continue;
            if (!scaleMax.TryGetValue(def.ScaleGroup, out var catMax)) continue;

            float v = history.ReadValue(last, m);
            if (float.IsNaN(v)) continue;

            float norm = catMax > 0 ? v / catMax : 0f;
            float y = innerBottom - norm * innerHeight;

            var sprite = _legend.ResolveIcon(def);
            if (sprite == null) continue;

            candidates.Add((def, y, sprite));
        }

        // Sort by y and chain into groups where each adjacent pair's preferred
        // y differs by less than one icon height — those would overlap if
        // placed at their natural positions.
        candidates.Sort((a, b) => a.Y.CompareTo(b.Y));
        var stillVisible = new System.Collections.Generic.HashSet<string>();
        float gutterX = rect.xMax - GutterWidth + 3;

        int i0 = 0;
        while (i0 < candidates.Count)
        {
            int i1 = i0;
            while (i1 + 1 < candidates.Count &&
                   candidates[i1 + 1].Y - candidates[i1].Y < EndIconSize)
                i1++;

            int n = i1 - i0 + 1;
            // Center the stack on the midpoint of the group's preferred y's;
            // each icon's center offsets by IconSize from its neighbour,
            // symmetrically around the group center. Single-icon groups keep
            // their exact y.
            float groupCenter = (candidates[i0].Y + candidates[i1].Y) * 0.5f;
            float stackHeight = (n - 1) * EndIconSize;
            float firstCenter = groupCenter - stackHeight * 0.5f;

            for (int i = i0; i <= i1; i++)
            {
                var c = candidates[i];
                float centerY = firstCenter + (i - i0) * EndIconSize;
                // Keep icons inside the chart vertically.
                centerY = Mathf.Clamp(centerY,
                    draw.y + EndIconSize / 2f,
                    draw.yMax - EndIconSize / 2f);

                if (!_endIcons.TryGetValue(c.Def.Id, out var img))
                {
                    img = new Image { sprite = c.Sprite, pickingMode = PickingMode.Ignore };
                    img.style.position = Position.Absolute;
                    img.style.width = EndIconSize;
                    img.style.height = EndIconSize;
                    _element.Add(img);
                    _endIcons[c.Def.Id] = img;
                }
                else
                {
                    img.sprite = c.Sprite;
                }
                img.style.display = DisplayStyle.Flex;
                img.style.left = gutterX;
                img.style.top = centerY - EndIconSize / 2f;

                // Companion value label sits just below the icon in the gutter.
                if (!_endLabels.TryGetValue(c.Def.Id, out var vlabel))
                {
                    vlabel = new Label("") { pickingMode = PickingMode.Ignore };
                    vlabel.style.position = Position.Absolute;
                    vlabel.style.fontSize = 10;
                    vlabel.style.color = new Color(0.92f, 0.86f, 0.72f);
                    vlabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                    vlabel.style.width = GutterWidth;
                    vlabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                    _element.Add(vlabel);
                    _endLabels[c.Def.Id] = vlabel;
                }
                int idx = _registry.IndexOf(c.Def.Id);
                float v = idx >= 0 ? history.ReadValue(last, idx) : float.NaN;
                vlabel.text = float.IsNaN(v)
                    ? "—"
                    : v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
                vlabel.style.display = DisplayStyle.Flex;
                vlabel.style.left = gutterX - (GutterWidth - EndIconSize) / 2f;
                vlabel.style.top = centerY + EndIconSize / 2f;

                stillVisible.Add(c.Def.Id);
            }
            i0 = i1 + 1;
        }

        foreach (var (id, img) in _endIcons)
            if (!stillVisible.Contains(id))
                img.style.display = DisplayStyle.None;
        foreach (var (id, lbl) in _endLabels)
            if (!stillVisible.Contains(id))
                lbl.style.display = DisplayStyle.None;
    }

    private void HideAllEndIcons()
    {
        foreach (var kv in _endIcons) kv.Value.style.display = DisplayStyle.None;
        foreach (var kv in _endLabels) kv.Value.style.display = DisplayStyle.None;
    }

    /// Refreshes the vertical cursor line and tooltip panel for the current
    /// pointer position. Picks the nearest sample to the cursor's x and lists
    /// the visible metrics' values at that sample.
    private void RefreshTooltip()
    {
        if (_element is null || _cursorLine is null || _tooltipBox is null || _tooltipHeader is null) return;

        if (!_cursorInside)
        {
            HideTooltip();
            return;
        }

        var rect = _element.contentRect;
        if (rect.width <= 0 || rect.height <= 0) { HideTooltip(); return; }
        var draw = DrawArea(rect);

        var history = _sampler.History;
        if (history.Count == 0) { HideTooltip(); return; }

        float latestT = history.ReadTimestamp(history.Count - 1);
        float earliestT = history.ReadTimestamp(0);
        float? lookback = _range.LookbackDays();
        float startT = lookback.HasValue
            ? System.Math.Max(latestT - lookback.Value, earliestT)
            : earliestT;
        int startIdx = history.FindFirstAtOrAfter(startT);
        int endIdx = history.Count;
        if (startIdx >= endIdx) { HideTooltip(); return; }

        float span = latestT - startT;
        if (span <= 0) { HideTooltip(); return; }

        // Map cursor x back to a timestamp — clamped within the drawing area,
        // so cursor positions over the gutter resolve to the most recent sample.
        float fx = Mathf.Clamp01((_cursorLocal.x - draw.x) / draw.width);
        float targetT = startT + fx * span;
        int nearest = startIdx;
        float nearestDelta = float.PositiveInfinity;
        for (int i = startIdx; i < endIdx; i++)
        {
            float d = Mathf.Abs(history.ReadTimestamp(i) - targetT);
            if (d < nearestDelta) { nearestDelta = d; nearest = i; }
        }

        float sampleT = history.ReadTimestamp(nearest);
        float sampleX = draw.x + ((sampleT - startT) / span) * draw.width;

        _cursorLine.style.display = DisplayStyle.Flex;
        _cursorLine.style.left = sampleX;

        _tooltipHeader.text = $"Day {sampleT:0.00}";

        // Rebuild the metric rows. Cheap enough; called on MouseMoveEvent only.
        foreach (var lbl in _tooltipRows) _tooltipBox.Remove(lbl);
        _tooltipRows.Clear();

        var metrics = _registry.Metrics;
        for (int m = 0; m < metrics.Count; m++)
        {
            var def = metrics[m];
            if (!_legend.VisibleMetricIds.Contains(def.Id)) continue;
            float v = history.ReadValue(nearest, m);
            string vs = float.IsNaN(v) ? "—" : v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            var row = new Label($"{_legend.ResolveDisplayName(def)}: {vs}");
            row.style.color = new StyleColor(GraphColors.ColorFor(def.Id, def.Category));
            row.style.fontSize = 11;
            _tooltipBox.Add(row);
            _tooltipRows.Add(row);
        }

        _tooltipBox.style.display = DisplayStyle.Flex;

        // Position the tooltip to the right of the cursor when possible;
        // flip left when near the right edge.
        const float offset = 10f;
        const float tooltipWidth = 200f;
        float tooltipX = sampleX + offset;
        if (tooltipX + tooltipWidth > rect.xMax) tooltipX = sampleX - offset - tooltipWidth;
        if (tooltipX < rect.x) tooltipX = rect.x + 4;
        _tooltipBox.style.left = tooltipX;
        _tooltipBox.style.top = Mathf.Max(4, _cursorLocal.y - 10);
    }

    private void HideTooltip()
    {
        if (_cursorLine != null) _cursorLine.style.display = DisplayStyle.None;
        if (_tooltipBox != null) _tooltipBox.style.display = DisplayStyle.None;
    }

    /// Inner drawing area (chart rect minus the right-side gutter where
    /// line-end icons live). All drawing + sample→x mapping uses this.
    private static Rect DrawArea(Rect contentRect)
    {
        float w = contentRect.width - GutterWidth;
        if (w < 0) w = 0;
        return new Rect(contentRect.x, contentRect.y, w, contentRect.height);
    }

    private void Draw(MeshGenerationContext ctx)
    {
        var rect = ctx.visualElement.contentRect;
        if (rect.width <= 0 || rect.height <= 0) return;
        var draw = DrawArea(rect);

        var history = _sampler.History;
        if (history.Count == 0) return;

        float latestTimestamp = history.ReadTimestamp(history.Count - 1);
        float earliestTimestamp = history.ReadTimestamp(0);
        float? lookback = _range.LookbackDays();
        float startTimestamp = lookback.HasValue
            ? System.Math.Max(latestTimestamp - lookback.Value, earliestTimestamp)
            : earliestTimestamp;

        int startIdx = history.FindFirstAtOrAfter(startTimestamp);
        int endIdx = history.Count;
        if (startIdx >= endIdx) return;

        DrawWeatherBands(ctx, draw, history, startIdx, endIdx, startTimestamp, latestTimestamp);
        DrawGridlines(ctx, draw);
        DrawLines(ctx, draw, history, startIdx, endIdx, startTimestamp, latestTimestamp);
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

        // Pass 1: compute per-ScaleGroup max across every visible metric in
        // that group. Min is pinned to 0 so y=0 always represents "no stock /
        // no beavers / no hunger satisfaction". ScaleGroup defaults to the
        // Category name but metrics can override (e.g. Bots shares the
        // Population scale while having its own legend section).
        var scaleMax = new Dictionary<string, float>(StringComparer.Ordinal);
        var metrics = _registry.Metrics;
        for (int m = 0; m < metrics.Count; m++)
        {
            var def = metrics[m];
            if (!_legend.VisibleMetricIds.Contains(def.Id)) continue;

            scaleMax.TryGetValue(def.ScaleGroup, out var cur);
            for (int i = startIdx; i < endIdx; i++)
            {
                float v = history.ReadValue(i, m);
                if (float.IsNaN(v)) continue;
                if (v > cur) cur = v;
            }
            scaleMax[def.ScaleGroup] = cur;
        }

        // Top gets a small inset so a value at catMax isn't clipped.
        // Bottom stays flush so y=0 always sits at the real chart bottom.
        const float topInset = 6f;
        float innerTop = rect.y + topInset;
        float innerBottom = rect.y + rect.height;
        float innerHeight = innerBottom - innerTop;

        // Pass 2: draw each visible metric using its category's shared scale.
        for (int m = 0; m < metrics.Count; m++)
        {
            var def = metrics[m];
            if (!_legend.VisibleMetricIds.Contains(def.Id)) continue;
            if (!scaleMax.TryGetValue(def.ScaleGroup, out var catMax)) continue;

            Color color = GraphColors.ColorFor(def.Id, def.Category);

            bool havePrev = false;
            Vector2 prev = default;
            for (int i = startIdx; i < endIdx; i++)
            {
                float v = history.ReadValue(i, m);
                if (float.IsNaN(v)) { havePrev = false; continue; }
                float t = history.ReadTimestamp(i);
                float x = rect.x + ((t - startT) / span) * rect.width;
                // 0 always anchors to the bottom of the chart; catMax to the
                // top. A value of catMax/2 lands halfway up regardless of what
                // other metrics in the category are doing.
                float norm = catMax > 0 ? v / catMax : 0f;
                float y = innerBottom - norm * innerHeight;

                FillRect(ctx, new Rect(x - 1, y - 1, 2, 2), color);
                if (havePrev)
                    DrawSegment(ctx, prev, new Vector2(x, y), color, thickness: 1f);
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

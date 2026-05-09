using LogBook.Metrics;
using UnityEngine;
using UnityEngine.UIElements;

namespace LogBook.UI;

public sealed class LogBookChart
{
    // Weather-band tints, semi-transparent so the wooden panel grain reads
    // through. Hues match the in-game `weather-progress-{dry,badtide}` pills.
    private const float WeatherBandAlpha = 0.55f;
    private const float WeatherBandRadius = 16f;
    private static readonly Color DroughtColor = WithAlpha(new(1.000f, 0.608f, 0.122f), WeatherBandAlpha);  // #ff9b1f
    private static readonly Color BadtideColor = WithAlpha(new(0.470f, 0.059f, 0.000f), WeatherBandAlpha);  // #780f00

    private static Color WithAlpha(Color c, float a) => new(c.r, c.g, c.b, a);

    private readonly MetricSampler _sampler;
    private readonly LogBookRangeSelector _range;
    private readonly MetricRegistry _registry;
    private readonly LogBookLegend _legend;
    private readonly GameIcons _icons;

    private VisualElement? _element;
    private bool _subscribed;

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
    private const float EndLabelHeight = 14f;
    // Vertical footprint of one marker (icon + label beneath it). Used as
    // the ideal inter-marker spacing during stacking. If there genuinely
    // isn't enough room for n * MarkerHeight, the stacker compresses the
    // pitch uniformly so everything still fits rather than spilling off.
    private const float MarkerHeight = EndIconSize + EndLabelHeight;
    private const float GutterWidth = EndIconSize + 6f; // icon + padding
    // Pool of gutter icons + value labels. Grows as needed; unused entries
    // are display:None. Every relevant style is reset on each use so a
    // re-enabled metric doesn't pick up the previous holder's state.
    private readonly System.Collections.Generic.List<Image> _endIconPool = new();
    private readonly System.Collections.Generic.List<Label> _endLabelPool = new();

    public LogBookChart(
        MetricSampler sampler,
        LogBookRangeSelector range,
        MetricRegistry registry,
        LogBookLegend legend,
        GameIcons icons)
    {
        _sampler = sampler;
        _range = range;
        _registry = registry;
        _legend = legend;
        _icons = icons;
    }

    public VisualElement Build()
    {
        _element = new VisualElement { name = "graphs-chart" };
        _element.style.flexGrow = 1;
        _element.generateVisualContent += Draw;

        // Subscribe once per chart lifetime — Build may be called on each
        // window Open, and subscribing there would leak the prior element
        // through the singleton's delegate list.
        if (!_subscribed)
        {
            _range.Changed += Repaint;
            _legend.Changed += Repaint;
            _sampler.OnSampled += Repaint;
            _subscribed = true;
        }
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
        if (_cursorInside) RefreshTooltip();
    }

    /// Places a gutter icon at the right edge of the chart for every
    /// visible metric, anchored to its line's final y. Resolves overlap by
    /// stacking conflicting markers vertically.
    private void UpdateEndIcons()
    {
        if (_element is null) return;

        var rect = _element.contentRect;
        if (rect.width <= 0 || rect.height <= 0) { HideAllEndPool(); return; }
        var draw = DrawArea(rect);

        var history = _sampler.HistoryFor(_range.LookbackDays());
        if (history.Count == 0) { HideAllEndPool(); return; }

        float latestT = history.ReadTimestamp(history.Count - 1);
        // Use the full selected lookback. If history doesn't fill the
        // window, real samples appear on the right and the left stays empty.
        float startT = latestT - _range.LookbackDays();
        int startIdx = history.FindFirstAtOrAfter(startT);
        int endIdx = history.Count;
        if (startIdx >= endIdx) { HideAllEndPool(); return; }

        var ranges = ComputeScaleRanges(history, startIdx, endIdx);
        var metrics = _registry.Metrics;

        const float topInset = 6f;
        float innerTop = draw.y + topInset;
        float innerBottom = draw.y + draw.height;
        float innerHeight = innerBottom - innerTop;

        // Sprite may be null for metrics without a native icon — those
        // render as a filled colored block in the gutter instead.
        var candidates = new System.Collections.Generic.List<(MetricDefinition Def, float Y, Sprite? Sprite)>();
        int last = endIdx - 1;

        for (int m = 0; m < metrics.Count; m++)
        {
            var def = metrics[m];
            if (!_legend.VisibleMetricIds.Contains(def.Id)) continue;
            if (!ranges.TryGetValue(def.ScaleGroup, out var range)) continue;

            // On coarser tiers, the latest sample is a bucket average and
            // can be NaN if any hour in the bucket failed. Walk back to the
            // most-recent non-NaN value so the marker still anchors to real
            // data.
            float v = LatestNonNaN(history, m, startIdx, last);
            if (float.IsNaN(v)) continue;

            float norm = range.Span > 0 ? (v - range.Min) / range.Span : 0f;
            float y = innerBottom - norm * innerHeight;

            candidates.Add((def, y, _legend.ResolveIcon(def)));
        }

        // Forward pass anchors at minY and pushes each marker below the
        // previous; backward pass clamps the last at maxY and pulls earlier
        // ones up to clear the next. If [minY,maxY] can't fit them all,
        // late markers overlap earlier ones.
        candidates.Sort((a, b) => a.Y.CompareTo(b.Y));
        float gutterX = rect.xMax - GutterWidth + 3;

        float minY = draw.y + EndIconSize / 2f;
        float maxY = draw.yMax - EndIconSize / 2f - EndLabelHeight;

        float[] ys = new float[candidates.Count];
        float cursor = minY;
        for (int i = 0; i < candidates.Count; i++)
        {
            ys[i] = Mathf.Max(candidates[i].Y, cursor);
            cursor = ys[i] + MarkerHeight;
        }
        for (int i = ys.Length - 1; i >= 0; i--)
        {
            float upper = (i + 1 < ys.Length) ? ys[i + 1] - MarkerHeight : maxY;
            if (ys[i] > upper) ys[i] = upper;
        }

        // Grow the pools to cover all candidates. One-time style setup on
        // new entries; per-use mutations come below.
        while (_endIconPool.Count < candidates.Count)
        {
            var img = new Image { pickingMode = PickingMode.Ignore };
            img.style.position = Position.Absolute;
            img.style.width = EndIconSize;
            img.style.height = EndIconSize;
            _element.Add(img);
            _endIconPool.Add(img);
        }
        while (_endLabelPool.Count < candidates.Count)
        {
            var lbl = new Label("") { pickingMode = PickingMode.Ignore };
            lbl.style.position = Position.Absolute;
            lbl.style.fontSize = 10;
            lbl.style.color = new Color(0.92f, 0.86f, 0.72f);
            lbl.style.unityTextAlign = TextAnchor.MiddleCenter;
            lbl.style.width = GutterWidth;
            lbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            _element.Add(lbl);
            _endLabelPool.Add(lbl);
        }

        for (int iIdx = 0; iIdx < candidates.Count; iIdx++)
        {
            var c = candidates[iIdx];
            float centerY = ys[iIdx];

            var img = _endIconPool[iIdx];
            img.style.left = gutterX;
            img.style.top = centerY - EndIconSize / 2f;
            // Reset BOTH sprite and backgroundColor — the slot may be reused
            // for a different metric or after a visibility toggle.
            if (c.Sprite == null)
            {
                img.sprite = null;
                img.style.backgroundColor = new StyleColor(GraphColors.ColorFor(c.Def.Id, c.Def.Category));
                img.tintColor = Color.white;
            }
            else
            {
                img.sprite = c.Sprite;
                img.style.backgroundColor = new StyleColor(Color.clear);
                // Wellbeing / hunger / thirst share a warm-gold tint so
                // they read as a group.
                img.tintColor = c.Def.Category == MetricCategory.Wellbeing
                    ? GraphColors.WellbeingTint
                    : Color.white;
            }
            img.style.display = DisplayStyle.Flex;

            int idx = _registry.IndexOf(c.Def.Id);
            float v = idx >= 0
                ? LatestNonNaN(history, idx, startIdx, last)
                : float.NaN;
            var vlabel = _endLabelPool[iIdx];
            vlabel.text = float.IsNaN(v)
                ? "—"
                : v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            vlabel.style.left = gutterX - (GutterWidth - EndIconSize) / 2f;
            vlabel.style.top = centerY + EndIconSize / 2f;
            vlabel.style.display = DisplayStyle.Flex;
        }

        // Hide any pool entries beyond this render's candidate count.
        for (int i = candidates.Count; i < _endIconPool.Count; i++)
            _endIconPool[i].style.display = DisplayStyle.None;
        for (int i = candidates.Count; i < _endLabelPool.Count; i++)
            _endLabelPool[i].style.display = DisplayStyle.None;
    }

    /// Walks backward through the window and returns the first non-NaN
    /// value, or NaN if every sample for the metric is NaN. Used so a
    /// single NaN-poisoned bucket on Mid/Old doesn't drop the gutter marker.
    private static float LatestNonNaN(MetricHistory history, int metricIdx, int startIdx, int endIdx)
    {
        for (int i = endIdx; i >= startIdx; i--)
        {
            float v = history.ReadValue(i, metricIdx);
            if (!float.IsNaN(v)) return v;
        }
        return float.NaN;
    }

    private void HideAllEndPool()
    {
        for (int i = 0; i < _endIconPool.Count; i++)
            _endIconPool[i].style.display = DisplayStyle.None;
        for (int i = 0; i < _endLabelPool.Count; i++)
            _endLabelPool[i].style.display = DisplayStyle.None;
    }


    /// Repositions the cursor line + tooltip for the current pointer
    /// position. Picks the nearest sample to the cursor's x and lists every
    /// visible metric's value at that sample.
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

        var history = _sampler.HistoryFor(_range.LookbackDays());
        if (history.Count == 0) { HideTooltip(); return; }

        float latestT = history.ReadTimestamp(history.Count - 1);
        float startT = latestT - _range.LookbackDays();
        int startIdx = history.FindFirstAtOrAfter(startT);
        int endIdx = history.Count;
        if (startIdx >= endIdx) { HideTooltip(); return; }

        float span = latestT - startT;
        if (span <= 0) { HideTooltip(); return; }

        // Map cursor x back to a timestamp, clamped to the drawing area
        // so cursor positions over the gutter snap to the latest sample.
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

        // "Day D:HH" — round to nearest hour so fp drift doesn't slide
        // hour 0 of day N+1 back to hour 23 of day N.
        int totalHours = Mathf.RoundToInt(sampleT * 24f);
        int day = totalHours / 24;
        int hour = totalHours % 24;
        _tooltipHeader.text = $"Day {day}:{hour:00}";

        // RemoveFromHierarchy is safe if the row already lost its parent;
        // Remove() throws on non-children.
        foreach (var lbl in _tooltipRows) lbl.RemoveFromHierarchy();
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

        // Tooltip sits right of the cursor, flipping left near the edge.
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

    private readonly struct ScaleRange
    {
        public readonly float Min;
        public readonly float Max;
        public ScaleRange(float min, float max) { Min = min; Max = max; }
        public float Span => Max - Min;
    }

    /// Computes a per-ScaleGroup y-axis range for the visible window.
    /// Bounds are soft: a metric's `FixedMin` / `FixedMax` anchor the axis
    /// at natural endpoints, but observed samples outside that range expand
    /// it — real data is never clipped. Floor defaults to 0 and drops below
    /// when a metric goes negative or declares a lower bound. Groups with
    /// no visible metric are omitted.
    private Dictionary<string, ScaleRange> ComputeScaleRanges(
        MetricHistory history, int startIdx, int endIdx)
    {
        var observedMin = new Dictionary<string, float>(StringComparer.Ordinal);
        var observedMax = new Dictionary<string, float>(StringComparer.Ordinal);
        var declaredMin = new Dictionary<string, float>(StringComparer.Ordinal);
        var declaredMax = new Dictionary<string, float>(StringComparer.Ordinal);

        var metrics = _registry.Metrics;
        for (int m = 0; m < metrics.Count; m++)
        {
            var def = metrics[m];
            if (!_legend.VisibleMetricIds.Contains(def.Id)) continue;

            if (def.FixedMax.HasValue)
            {
                if (!declaredMax.TryGetValue(def.ScaleGroup, out var prior) || def.FixedMax.Value > prior)
                    declaredMax[def.ScaleGroup] = def.FixedMax.Value;
            }
            if (def.FixedMin.HasValue)
            {
                if (!declaredMin.TryGetValue(def.ScaleGroup, out var prior) || def.FixedMin.Value < prior)
                    declaredMin[def.ScaleGroup] = def.FixedMin.Value;
            }

            if (!observedMin.ContainsKey(def.ScaleGroup))
            {
                observedMin[def.ScaleGroup] = 0f;
                observedMax[def.ScaleGroup] = 0f;
            }
            float gMin = observedMin[def.ScaleGroup];
            float gMax = observedMax[def.ScaleGroup];

            for (int i = startIdx; i < endIdx; i++)
            {
                float v = history.ReadValue(i, m);
                if (float.IsNaN(v)) continue;
                if (v < gMin) gMin = v;
                if (v > gMax) gMax = v;
            }
            observedMin[def.ScaleGroup] = gMin;
            observedMax[def.ScaleGroup] = gMax;
        }

        var ranges = new Dictionary<string, ScaleRange>(StringComparer.Ordinal);
        foreach (var kv in observedMax)
        {
            float max = kv.Value;
            if (declaredMax.TryGetValue(kv.Key, out var declMax) && declMax > max) max = declMax;
            float min = observedMin[kv.Key];
            if (declaredMin.TryGetValue(kv.Key, out var declMin) && declMin < min) min = declMin;
            ranges[kv.Key] = new ScaleRange(min, max);
        }
        return ranges;
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

        var history = _sampler.HistoryFor(_range.LookbackDays());
        if (history.Count == 0) return;

        float latestTimestamp = history.ReadTimestamp(history.Count - 1);
        float startTimestamp = latestTimestamp - _range.LookbackDays();

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

        var ranges = ComputeScaleRanges(history, startIdx, endIdx);
        var metrics = _registry.Metrics;

        // Top inset so a value at catMax isn't clipped; bottom flush so
        // y=0 always sits at the real chart bottom.
        const float topInset = 6f;
        float innerTop = rect.y + topInset;
        float innerBottom = rect.y + rect.height;
        float innerHeight = innerBottom - innerTop;

        for (int m = 0; m < metrics.Count; m++)
        {
            var def = metrics[m];
            if (!_legend.VisibleMetricIds.Contains(def.Id)) continue;
            if (!ranges.TryGetValue(def.ScaleGroup, out var range)) continue;

            Color color = GraphColors.ColorFor(def.Id, def.Category);

            bool havePrev = false;
            Vector2 prev = default;
            for (int i = startIdx; i < endIdx; i++)
            {
                float v = history.ReadValue(i, m);
                if (float.IsNaN(v)) { havePrev = false; continue; }
                float t = history.ReadTimestamp(i);
                float x = rect.x + ((t - startT) / span) * rect.width;
                float norm = range.Span > 0 ? (v - range.Min) / range.Span : 0f;
                float y = innerBottom - norm * innerHeight;

                FillRect(ctx, new Rect(x - 1, y - 1, 2, 2), color);
                if (havePrev)
                    DrawSegment(ctx, prev, new Vector2(x, y), color, thickness: 2f);
                prev = new Vector2(x, y);
                havePrev = true;
            }
        }
    }

    /// Draws a line segment as a chain of overlapping axis-aligned stamps.
    /// Rotated-quad meshes render unreliably under Timberborn's UIToolkit
    /// pipeline; axis-aligned FillRect always paints.
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

    private void DrawWeatherBands(
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
                    _                            => default,
                };
                if (color.a > 0)
                    FillRoundedRect(ctx, new Rect(x0, rect.y, x1 - x0, rect.height), WeatherBandRadius, color);
            }
            runStart = i;
            runWeather = w;
        }
    }

    /// Axis-aligned rounded rectangle: three rectangle fills (center plus
    /// top/bottom strips inset by radius) and four quarter-circle fans.
    private static void FillRoundedRect(
        MeshGenerationContext ctx, Rect rect, float radius, Color color)
    {
        if (color.a <= 0 || rect.width <= 0 || rect.height <= 0) return;

        radius = Mathf.Min(radius, Mathf.Min(rect.width, rect.height) * 0.5f);
        if (radius <= 0)
        {
            FillRect(ctx, rect, color);
            return;
        }

        FillRect(ctx, new Rect(rect.x, rect.y + radius, rect.width, rect.height - 2 * radius), color);
        FillRect(ctx, new Rect(rect.x + radius, rect.y, rect.width - 2 * radius, radius), color);
        FillRect(ctx, new Rect(rect.x + radius, rect.yMax - radius, rect.width - 2 * radius, radius), color);

        // y grows downward in UIToolkit, but standard sin/cos still works —
        // "top" reads as the lower-y side.
        FillCornerFan(ctx, new Vector2(rect.x + radius,     rect.y + radius),     radius, 180f, 270f, color); // top-left
        FillCornerFan(ctx, new Vector2(rect.xMax - radius,  rect.y + radius),     radius, 270f, 360f, color); // top-right
        FillCornerFan(ctx, new Vector2(rect.xMax - radius,  rect.yMax - radius),  radius,   0f,  90f, color); // bottom-right
        FillCornerFan(ctx, new Vector2(rect.x + radius,     rect.yMax - radius),  radius,  90f, 180f, color); // bottom-left
    }

    private static void FillCornerFan(
        MeshGenerationContext ctx, Vector2 center, float radius,
        float startAngleDeg, float endAngleDeg, Color color)
    {
        if (color.a <= 0 || radius <= 0) return;
        const int segments = 4;
        var mesh = ctx.Allocate(segments + 2, segments * 3);
        mesh.SetNextVertex(new Vertex { position = new Vector3(center.x, center.y, Vertex.nearZ), tint = color });
        for (int i = 0; i <= segments; i++)
        {
            float t = i / (float)segments;
            float angle = Mathf.Lerp(startAngleDeg, endAngleDeg, t) * Mathf.Deg2Rad;
            mesh.SetNextVertex(new Vertex
            {
                position = new Vector3(
                    center.x + Mathf.Cos(angle) * radius,
                    center.y + Mathf.Sin(angle) * radius,
                    Vertex.nearZ),
                tint = color,
            });
        }
        for (ushort i = 0; i < segments; i++)
        {
            mesh.SetNextIndex(0);
            mesh.SetNextIndex((ushort)(i + 1));
            mesh.SetNextIndex((ushort)(i + 2));
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

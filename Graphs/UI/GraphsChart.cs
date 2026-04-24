using Graphs.Metrics;
using UnityEngine;
using UnityEngine.UIElements;

namespace Graphs.UI;

public sealed class GraphsChart
{
    // Fallback tints used until we've successfully sampled the drought /
    // badtide notification banners. Once sampled, the banner's average hue
    // replaces the fallback (still applied at low alpha for the band fill).
    private static readonly Color FallbackDroughtColor = new(0.88f, 0.45f, 0.12f);
    private static readonly Color FallbackBadtideColor = new(0.55f, 0.20f, 0.70f);
    private const float WeatherBandAlpha = 0.22f;

    private Color _droughtColor = WithAlpha(FallbackDroughtColor, WeatherBandAlpha);
    private Color _badtideColor = WithAlpha(FallbackBadtideColor, WeatherBandAlpha);
    private bool _weatherColorsSampled;

    private const float WeatherBandRadius = 16f;

    private static Color WithAlpha(Color c, float a) => new(c.r, c.g, c.b, a);

    // Fixed tint applied to every Wellbeing-category icon so the three
    // metrics (wellbeing / hunger / thirst) read as a single thematic group.
    private static readonly Color WellbeingTint = new(0.96f, 0.80f, 0.48f);

    private readonly MetricSampler _sampler;
    private readonly GraphsRangeSelector _range;
    private readonly MetricRegistry _registry;
    private readonly GraphsLegend _legend;
    private readonly GameIcons _icons;

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
    private const float EndLabelHeight = 14f;
    // Vertical footprint of one marker (icon + label beneath it). Used as
    // the ideal inter-marker spacing during stacking. If there genuinely
    // isn't enough room for n * MarkerHeight, the stacker compresses the
    // pitch uniformly so everything still fits rather than spilling off.
    private const float MarkerHeight = EndIconSize + EndLabelHeight;
    private const float GutterWidth = EndIconSize + 6f; // icon + padding
    private readonly System.Collections.Generic.Dictionary<string, Image> _endIcons = new();
    private readonly System.Collections.Generic.Dictionary<string, Label> _endLabels = new();

    public GraphsChart(
        MetricSampler sampler,
        GraphsRangeSelector range,
        MetricRegistry registry,
        GraphsLegend legend,
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
        // Transparent so the game's panel background (wood frame) shows
        // through — the chart paints grid, weather bands, and lines itself.
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
        if (_cursorInside) RefreshTooltip();
    }

    private void OnRepaintTrigger()
    {
        _element?.MarkDirtyRepaint();
        UpdateEndIcons();
        if (_cursorInside) RefreshTooltip();
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
        // Always use the full selected lookback window — don't clamp to the
        // earliest sample. If we have less data than the window, the real
        // samples will appear on the right side and the left will be empty.
        float startT = latestT - _range.LookbackDays();
        int startIdx = history.FindFirstAtOrAfter(startT);
        int endIdx = history.Count;
        if (startIdx >= endIdx) { HideAllEndIcons(); return; }

        var ranges = ComputeScaleRanges(history, startIdx, endIdx);
        var metrics = _registry.Metrics;

        const float topInset = 6f;
        float innerTop = draw.y + topInset;
        float innerBottom = draw.y + draw.height;
        float innerHeight = innerBottom - innerTop;

        // Gather candidates with their preferred y anchored to the line end.
        // Sprite may be null for metrics without a native icon — those render
        // as a filled colored block in the gutter instead.
        var candidates = new System.Collections.Generic.List<(MetricDefinition Def, float Y, Sprite? Sprite)>();
        int last = endIdx - 1;

        for (int m = 0; m < metrics.Count; m++)
        {
            var def = metrics[m];
            if (!_legend.VisibleMetricIds.Contains(def.Id)) continue;
            if (!ranges.TryGetValue(def.ScaleGroup, out var range)) continue;

            float v = history.ReadValue(last, m);
            if (float.IsNaN(v)) continue;

            float norm = range.Span > 0 ? (v - range.Min) / range.Span : 0f;
            float y = innerBottom - norm * innerHeight;

            candidates.Add((def, y, _legend.ResolveIcon(def)));
        }

        // Sort by preferred y ascending, then pack vertically so adjacent
        // markers never overlap. Two-pass:
        //   forward — y_i = max(preferred_i, y_{i-1} + MarkerHeight), clamped above
        //   backward — if the last marker overflows the bottom, pull earlier
        //              markers up by y_i = min(y_i, y_{i+1} - MarkerHeight)
        // Together this guarantees no overlaps, every marker inside the chart
        // when there's room, and sensible fallback when there isn't.
        candidates.Sort((a, b) => a.Y.CompareTo(b.Y));
        var stillVisible = new System.Collections.Generic.HashSet<string>();
        float gutterX = rect.xMax - GutterWidth + 3;

        float minY = draw.y + EndIconSize / 2f;
        float maxY = draw.yMax - EndIconSize / 2f - EndLabelHeight;

        float[] ys = new float[candidates.Count];
        for (int i = 0; i < candidates.Count; i++) ys[i] = candidates[i].Y;

        // If the ideal MarkerHeight (icon + label) would overflow the
        // available chart height, compress the inter-marker pitch uniformly
        // so all markers still fit. Labels end up slightly overlapping the
        // icon below them, but nothing falls off the chart.
        float effectiveMH = MarkerHeight;
        if (ys.Length > 1)
        {
            float available = maxY - minY;
            float needed = (ys.Length - 1) * MarkerHeight;
            if (needed > available) effectiveMH = available / (ys.Length - 1);
        }

        // forward push-down
        if (ys.Length > 0) ys[0] = Mathf.Max(ys[0], minY);
        for (int i = 1; i < ys.Length; i++)
            ys[i] = Mathf.Max(ys[i], ys[i - 1] + effectiveMH);

        // backward pull-up if the bottom overflowed
        if (ys.Length > 0 && ys[ys.Length - 1] > maxY)
        {
            ys[ys.Length - 1] = maxY;
            for (int i = ys.Length - 2; i >= 0; i--)
                ys[i] = Mathf.Min(ys[i], ys[i + 1] - effectiveMH);
            // second forward pass in case the top now overflows
            if (ys[0] < minY)
            {
                ys[0] = minY;
                for (int i = 1; i < ys.Length; i++)
                    ys[i] = Mathf.Max(ys[i], ys[i - 1] + effectiveMH);
            }
        }

        for (int iIdx = 0; iIdx < candidates.Count; iIdx++)
        {
            var c = candidates[iIdx];
            float centerY = ys[iIdx];

                if (!_endIcons.TryGetValue(c.Def.Id, out var img))
                {
                    img = new Image { pickingMode = PickingMode.Ignore };
                    img.style.position = Position.Absolute;
                    img.style.width = EndIconSize;
                    img.style.height = EndIconSize;
                    _element.Add(img);
                    _endIcons[c.Def.Id] = img;
                }
                img.sprite = c.Sprite;
                if (c.Sprite == null)
                {
                    // Fallback when no sprite resolves: plain filled square
                    // in the line color.
                    var color = GraphColors.ColorFor(c.Def.Id, c.Def.Category);
                    img.style.backgroundColor = new StyleColor(color);
                    img.tintColor = Color.white;
                }
                else
                {
                    img.style.backgroundColor = StyleKeyword.Null;
                    // Metrics in the Wellbeing category use a fixed category
                    // tint (warm gold) across all three (wellbeing / hunger /
                    // thirst) so they read as a group. Other metrics' icons
                    // pass through with their natural game colors.
                    img.tintColor = c.Def.Category == MetricCategory.Wellbeing
                        ? WellbeingTint
                        : Color.white;
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

    /// Samples the average hue of each weather notification banner so the
    /// band color matches the sprite the user would see in the HUD. Runs
    /// once on first paint; silently falls back to the hardcoded tints if
    /// the sprite texture isn't readable (common for asset-bundle sprites).
    private void EnsureWeatherColorsSampled()
    {
        if (_weatherColorsSampled) return;
        _weatherColorsSampled = true;

        var drought = TrySampleDominantColor(_icons.TryGet("weather.drought"));
        if (drought.HasValue) _droughtColor = WithAlpha(drought.Value, WeatherBandAlpha);

        var badtide = TrySampleDominantColor(_icons.TryGet("weather.badtide"));
        if (badtide.HasValue) _badtideColor = WithAlpha(badtide.Value, WeatherBandAlpha);
    }

    private static Color? TrySampleDominantColor(Sprite? sprite)
    {
        if (sprite == null) return null;
        try
        {
            var src = sprite.texture;
            if (src == null) return null;
            var srcRect = sprite.textureRect;
            int w = (int)srcRect.width, h = (int)srcRect.height;
            if (w <= 0 || h <= 0) return null;

            // Blit through a RenderTexture so we can read pixels regardless
            // of whether the source texture has Read/Write Enabled. Game
            // asset-bundle sprites are almost always non-readable, so this
            // is the only reliable way to sample them at runtime.
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.Default, RenderTextureReadWrite.Linear);
            var prev = RenderTexture.active;
            try
            {
                Graphics.Blit(src, rt);
                RenderTexture.active = rt;
                var snap = new Texture2D(w, h, TextureFormat.RGBA32, false, linear: true);
                snap.ReadPixels(new Rect(srcRect.x, src.height - srcRect.y - h, w, h), 0, 0);
                snap.Apply();
                var pixels = snap.GetPixels();
                UnityEngine.Object.Destroy(snap);

                // Pick the most-saturated pixel (weighted by alpha). Banners
                // have lots of neutral / white content; a plain average
                // washes out the signature hue, while picking the peak
                // saturation gives the color the user actually notices.
                Color best = Color.black;
                float bestScore = -1f;
                for (int i = 0; i < pixels.Length; i++)
                {
                    var p = pixels[i];
                    if (p.a < 0.5f) continue;
                    float hi = Mathf.Max(p.r, Mathf.Max(p.g, p.b));
                    float lo = Mathf.Min(p.r, Mathf.Min(p.g, p.b));
                    float chroma = hi - lo;
                    float score = chroma * p.a * (hi + 0.2f); // favor bright, saturated
                    if (score > bestScore) { bestScore = score; best = p; }
                }
                if (bestScore <= 0f) return null;
                return new Color(best.r, best.g, best.b, 1f);
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
            }
        }
        catch
        {
            return null;
        }
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
        float startT = latestT - _range.LookbackDays();
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

        // Rebuild the metric rows. RemoveFromHierarchy is safe even if the
        // row somehow lost its parent (Remove() throws when it's not a child).
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

    private readonly struct ScaleRange
    {
        public readonly float Min;
        public readonly float Max;
        public ScaleRange(float min, float max) { Min = min; Max = max; }
        public float Span => Max - Min;
    }

    /// Computes a per-ScaleGroup y-axis range for the visible window.
    /// Bounds are **soft**: a metric's `FixedMin` / `FixedMax` anchor the
    /// axis at known natural bounds, but the chart still expands if the
    /// data goes outside them. Floor defaults to 0 so positive-only metrics
    /// don't lift off the baseline; it drops as needed when a metric goes
    /// negative or declares a lower bound. Groups with no visible metric
    /// are omitted.
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
            // Soft bounds: declared values anchor the axis, but observed
            // data outside them wins — the chart never clips real samples.
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

        EnsureWeatherColorsSampled();

        var history = _sampler.History;
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

        // Per-ScaleGroup range. Floor is 0 unless a metric goes negative
        // (wellbeing can dip below zero for miserable beavers). Ceiling is
        // the observed max or a metric-declared FixedMax (so 0..1
        // satisfaction lines don't stretch to fill the chart when tiny).
        var ranges = ComputeScaleRanges(history, startIdx, endIdx);
        var metrics = _registry.Metrics;

        // Top gets a small inset so a value at catMax isn't clipped.
        // Bottom stays flush so y=0 always sits at the real chart bottom.
        const float topInset = 6f;
        float innerTop = rect.y + topInset;
        float innerBottom = rect.y + rect.height;
        float innerHeight = innerBottom - innerTop;

        // Pass 2: draw each visible metric using its group's range.
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
                // The group's min anchors to the bottom of the chart; its
                // max to the top. Min defaults to 0 but drops below 0 when
                // wellbeing / other metrics go negative, so a negative line
                // plots below the zero baseline instead of running off-chart.
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
                    MetricHistory.WeatherDrought => _droughtColor,
                    MetricHistory.WeatherBadtide => _badtideColor,
                    _                            => default,
                };
                if (color.a > 0)
                    FillRoundedRect(ctx, new Rect(x0, rect.y, x1 - x0, rect.height), WeatherBandRadius, color);
            }
            runStart = i;
            runWeather = w;
        }
    }

    /// Axis-aligned rounded rectangle. Constructed as three axis-aligned
    /// quads (center + top strip + bottom strip) plus four quarter-circle
    /// triangle fans for the corners.
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

        // Full-width center strip (covers everything except the top and
        // bottom `radius` slabs, which need to accommodate the corner arcs).
        FillRect(ctx, new Rect(rect.x, rect.y + radius, rect.width, rect.height - 2 * radius), color);
        // Top + bottom strips, inset horizontally by radius so the corners
        // can round inward.
        FillRect(ctx, new Rect(rect.x + radius, rect.y, rect.width - 2 * radius, radius), color);
        FillRect(ctx, new Rect(rect.x + radius, rect.yMax - radius, rect.width - 2 * radius, radius), color);

        // Corner fans. y-axis is inverted in UIToolkit (y grows downward),
        // but standard sin/cos still works — we just read "top" as the
        // lower-y side and "bottom" as the higher-y side.
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

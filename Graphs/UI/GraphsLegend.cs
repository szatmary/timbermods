using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Graphs.Metrics;
using Timberborn.CoreUI;
using Timberborn.Goods;
using Timberborn.Localization;
using UnityEngine;
using UnityEngine.UIElements;

namespace Graphs.UI;

/// Scrollable legend of checkbox rows, grouped by metric category.
/// Goods are further sub-grouped by the game's own good-group ids
/// (e.g. Food, Ingredients, Logs, Metal, etc.) so related goods cluster.
/// Each goods row shows the game's icon + localized name.
public sealed class GraphsLegend
{
    private const string PrefsKey = "Graphs.VisibleMetricIds";

    private static readonly HashSet<string> DefaultVisible = new()
    {
        "good.Log", "good.Berries", "good.Water",
        "pop.total", "science.stored", "wellbeing.avg",
    };

    private readonly MetricRegistry _registry;
    private readonly IGoodService _goodService;
    private readonly ILoc _loc;
    private readonly GameIcons _icons;

    public HashSet<string> VisibleMetricIds { get; } = new();
    public event Action? Changed;

    private readonly Dictionary<string, Label> _valueLabels = new();
    private bool _defaultsApplied;

    public GraphsLegend(MetricRegistry registry, IGoodService goodService, ILoc loc, GameIcons icons)
    {
        _registry = registry;
        _goodService = goodService;
        _loc = loc;
        _icons = icons;
    }

    public VisualElement Build()
    {
        ApplyDefaultsIfNeeded();

        var scroll = new ScrollView(ScrollViewMode.Vertical);
        scroll.AddToClassList("game-scroll-view");
        scroll.style.flexGrow = 1;

        foreach (var categoryGroup in _registry.Metrics
                     .GroupBy(m => m.Category)
                     .OrderBy(g => (int)g.Key))
        {
            scroll.Add(BuildCategorySection(categoryGroup.Key, categoryGroup));
        }

        return scroll;
    }

    private void ApplyDefaultsIfNeeded()
    {
        if (_defaultsApplied) return;
        _defaultsApplied = true;

        // Restore the user's last-session selection if present. Otherwise
        // fall back to the curated defaults.
        var saved = PlayerPrefs.GetString(PrefsKey, "");
        HashSet<string>? restored = null;
        if (!string.IsNullOrEmpty(saved))
        {
            restored = new HashSet<string>(
                saved.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries),
                StringComparer.Ordinal);
        }

        foreach (var m in _registry.Metrics)
        {
            bool visible = restored != null
                ? restored.Contains(m.Id)
                : DefaultVisible.Contains(m.Id);
            if (visible) VisibleMetricIds.Add(m.Id);
        }
    }

    private void SavePrefs()
    {
        PlayerPrefs.SetString(PrefsKey, string.Join("|", VisibleMetricIds));
        PlayerPrefs.Save();
    }

    public void UpdateCurrentValues(Func<string, float> valueOf)
    {
        foreach (var pair in _valueLabels)
        {
            var v = valueOf(pair.Key);
            pair.Value.text = float.IsNaN(v) ? "—" : v.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }

    private VisualElement BuildCategorySection(
        MetricCategory category, IEnumerable<MetricDefinition> metrics)
    {
        var section = new VisualElement();
        section.style.marginBottom = 6;

        var header = new Label(category.ToString().ToUpperInvariant());
        header.style.color = new Color(0.75f, 0.75f, 0.80f);
        header.style.fontSize = 13;
        header.style.unityFontStyleAndWeight = FontStyle.Bold;
        header.style.marginBottom = 2;
        section.Add(header);

        if (category == MetricCategory.Goods)
            BuildGoodsGroups(section, metrics);
        else
            BuildSubGroups(section, metrics);

        return section;
    }

    /// Generic sub-grouping: partitions metrics by their `SubGroup` string
    /// and renders a small header per group. Metrics with no SubGroup fall
    /// into a single unlabelled bucket at the top.
    private void BuildSubGroups(VisualElement section, IEnumerable<MetricDefinition> metrics)
    {
        var groups = new List<(string? Name, List<MetricDefinition> Items)>();
        var indexByName = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var def in metrics)
        {
            var name = def.SubGroup;
            int idx;
            if (name != null && indexByName.TryGetValue(name, out idx))
            {
                groups[idx].Items.Add(def);
            }
            else
            {
                groups.Add((name, new List<MetricDefinition> { def }));
                if (name != null) indexByName[name] = groups.Count - 1;
            }
        }

        foreach (var (name, items) in groups)
        {
            if (!string.IsNullOrEmpty(name))
            {
                var groupHeader = new Label(name!.ToUpperInvariant());
                groupHeader.style.color = new Color(0.60f, 0.72f, 0.80f);
                groupHeader.style.fontSize = 11;
                groupHeader.style.marginTop = 4;
                groupHeader.style.marginLeft = 4;
                section.Add(groupHeader);
            }
            foreach (var def in items) section.Add(BuildRow(def));
        }
    }

    private void BuildGoodsGroups(VisualElement section, IEnumerable<MetricDefinition> metrics)
    {
        // Group goods by their GoodSpec.GoodGroupId.
        var groups = new SortedDictionary<string, List<MetricDefinition>>(StringComparer.Ordinal);
        var ungrouped = new List<MetricDefinition>();

        foreach (var def in metrics)
        {
            string? groupId = null;
            if (def.Id.StartsWith("good.", StringComparison.Ordinal))
            {
                var goodId = def.Id.Substring("good.".Length);
                try { groupId = _goodService.GetGood(goodId)?.GoodGroupId; } catch { }

                // Merge water-family goods so Water and Badwater sit together.
                if (goodId == "Water" || goodId == "Badwater")
                    groupId = "Water";
            }
            if (string.IsNullOrEmpty(groupId))
            {
                ungrouped.Add(def);
                continue;
            }
            if (!groups.TryGetValue(groupId!, out var list))
            {
                list = new List<MetricDefinition>();
                groups[groupId!] = list;
            }
            list.Add(def);
        }

        foreach (var (groupId, list) in groups)
        {
            var groupHeader = new Label(PrettifyGroupId(groupId));
            groupHeader.style.color = new Color(0.60f, 0.72f, 0.80f);
            groupHeader.style.fontSize = 11;
            groupHeader.style.marginTop = 4;
            groupHeader.style.marginLeft = 4;
            section.Add(groupHeader);

            foreach (var def in list) section.Add(BuildRow(def));
        }

        if (ungrouped.Count > 0)
        {
            var u = new Label("OTHER");
            u.style.color = new Color(0.55f, 0.55f, 0.55f);
            u.style.fontSize = 11;
            u.style.marginTop = 4;
            u.style.marginLeft = 4;
            section.Add(u);
            foreach (var def in ungrouped) section.Add(BuildRow(def));
        }
    }

    private static string PrettifyGroupId(string groupId)
    {
        // Group ids are typically CamelCase ids like "Food" or "Ingredients" —
        // show them uppercased with spaces inserted before interior capitals.
        if (string.IsNullOrEmpty(groupId)) return groupId;
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < groupId.Length; i++)
        {
            char c = groupId[i];
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(groupId[i - 1]))
                sb.Append(' ');
            sb.Append(c);
        }
        return sb.ToString().ToUpperInvariant();
    }

    private VisualElement BuildRow(MetricDefinition def)
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.height = 24;
        row.style.marginLeft = 6;

        string displayName = ResolveDisplayName(def, out Sprite? icon);

        // Icon on the far left — either the game's good-icon sprite (goods)
        // or a 20x20 swatch slot (other metrics) so rows align vertically.
        if (icon != null)
        {
            var img = new Image { sprite = icon };
            img.style.width = 20; img.style.height = 20;
            img.style.marginRight = 6;
            row.Add(img);
        }
        else
        {
            var spacer = new VisualElement();
            spacer.style.width = 20; spacer.style.height = 20;
            spacer.style.marginRight = 6;
            row.Add(spacer);
        }

        // Color swatch for the line.
        var swatch = new VisualElement();
        swatch.style.width = 10; swatch.style.height = 10;
        swatch.style.marginRight = 6;
        swatch.style.backgroundColor = new StyleColor(GraphColors.ColorFor(def.Id, def.Category));
        row.Add(swatch);

        // LocalizableToggle + .game-toggle class triggers the game's USS
        // rules that pull the native checkmark sprite (UI/Images/Buttons/
        // checkmark-*). _textLocKey must be set before VisualElementInitializer
        // runs or the localizer throws; the inline label is cleared post-init
        // because the metric name lives in a sibling Label.
        var toggle = new LocalizableToggle { value = VisibleMetricIds.Contains(def.Id) };
        toggle._textLocKey = "Graphs.Empty";
        toggle.AddToClassList("game-toggle");
        toggle.style.marginRight = 4;
        toggle.RegisterValueChangedCallback(evt =>
        {
            if (evt.newValue) VisibleMetricIds.Add(def.Id);
            else VisibleMetricIds.Remove(def.Id);
            SavePrefs();
            Changed?.Invoke();
        });
        row.Add(toggle);

        var name = new Label(displayName);
        name.style.flexGrow = 1;
        name.style.color = Color.white;
        name.style.fontSize = 12;
        row.Add(name);

        // Current-value label lives on the chart's gutter icon now, not in
        // the legend row. Registry entry kept empty so UpdateCurrentValues
        // remains a no-op for this metric.

        return row;
    }

    /// For goods, return the game's localized DisplayName and grab the icon.
    /// For other metrics, use the loc service to translate NameLocKey; if that
    /// returns the key itself (no translation loaded), strip the dotted prefix
    /// so rows read "Total" / "Adults" instead of "Graphs.Metric.Total".
    /// Public so the chart tooltip can share the same resolution logic.
    public string ResolveDisplayName(MetricDefinition def)
    {
        return ResolveDisplayName(def, out _);
    }

    /// Public so the chart can label its line-end positions with the
    /// same icons the legend rows use.
    public Sprite? ResolveIcon(MetricDefinition def)
    {
        ResolveDisplayName(def, out var icon);
        if (icon != null) return icon;
        // Thematic fallbacks for metrics without their own sprite: reuse
        // a good icon that represents the underlying need, or an existing
        // beaver sprite for aggregate wellbeing.
        if (def.Id == "need.hunger.avg") return TryGoodIcon("Berries");
        if (def.Id == "need.thirst.avg") return TryGoodIcon("Water");
        if (def.Id == "wellbeing.avg")   return _icons.TryGet("pop.total"); // ico-beavers
        return null;
    }

    private Sprite? TryGoodIcon(string goodId)
    {
        try { return _goodService.GetGood(goodId)?.Icon.Asset; }
        catch { return null; }
    }

    private string ResolveDisplayName(MetricDefinition def, out Sprite? icon)
    {
        icon = _icons.TryGet(def.Id);

        if (def.Category == MetricCategory.Goods &&
            def.Id.StartsWith("good.", StringComparison.Ordinal))
        {
            var goodId = def.Id.Substring("good.".Length);
            try
            {
                var spec = _goodService.GetGood(goodId);
                if (spec != null)
                {
                    icon = spec.Icon.Asset;
                    var display = spec.DisplayName.Value;
                    if (!string.IsNullOrEmpty(display)) return display;
                }
            }
            catch { }
            return goodId;
        }

        var fallback = def.NameLocKey;
        try
        {
            var t = _loc.T(def.NameLocKey);
            if (!string.IsNullOrEmpty(t) && t != def.NameLocKey) return t;
        }
        catch { }
        // Strip everything before the last '.' so "Graphs.Metric.Total" → "Total".
        int lastDot = fallback.LastIndexOf('.');
        return lastDot >= 0 && lastDot < fallback.Length - 1
            ? fallback.Substring(lastDot + 1)
            : fallback;
    }
}

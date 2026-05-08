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
                groupId = _goodService.GetGood(goodId)?.GoodGroupId;

                // Group Water + Badwater together.
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
        // CamelCase → uppercased with spaces before interior capitals.
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
        if (icon == null) icon = ResolveIcon(def);

        // Icon (or a same-sized spacer) on the far left so rows align.
        if (icon != null)
        {
            var img = new Image { sprite = icon };
            img.style.width = 20; img.style.height = 20;
            img.style.marginRight = 6;
            if (def.Category == MetricCategory.Wellbeing)
                img.tintColor = GraphColors.WellbeingTint;
            row.Add(img);
        }
        else
        {
            var spacer = new VisualElement();
            spacer.style.width = 20; spacer.style.height = 20;
            spacer.style.marginRight = 6;
            row.Add(spacer);
        }

        var swatch = new VisualElement();
        swatch.style.width = 10; swatch.style.height = 10;
        swatch.style.marginRight = 6;
        swatch.style.backgroundColor = new StyleColor(GraphColors.ColorFor(def.Id, def.Category));
        row.Add(swatch);

        // LocalizableToggle + .game-toggle class pulls the native
        // checkmark sprite via the game's USS. _textLocKey must be set
        // before VisualElementInitializer runs (the localizer throws
        // otherwise); we then clear the inline label since the metric
        // name lives in a sibling Label.
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

        // Clicking anywhere on the row flips the toggle — bigger hit target
        // than the checkbox itself. Skip when the click already landed on
        // the toggle so we don't double-toggle.
        row.RegisterCallback<ClickEvent>(evt =>
        {
            if (evt.target is VisualElement ve && IsDescendantOf(ve, toggle)) return;
            toggle.value = !toggle.value;
        });

        return row;
    }

    private static bool IsDescendantOf(VisualElement candidate, VisualElement ancestor)
    {
        for (var e = candidate; e != null; e = e.parent)
            if (e == ancestor) return true;
        return false;
    }

    /// Goods: return the localized DisplayName and grab the icon.
    /// Other metrics: translate NameLocKey via the loc service. If the loc
    /// service returns the key unchanged (no translation), strip the dotted
    /// prefix so rows read "Total" instead of "Graphs.Metric.Total".
    /// Shared with the chart tooltip.
    public string ResolveDisplayName(MetricDefinition def)
    {
        return ResolveDisplayName(def, out _);
    }

    /// Shared with the chart so line-end markers use the same icon as the
    /// legend row. Falls back to thematic good icons for need-based metrics
    /// that don't have their own sprite.
    public Sprite? ResolveIcon(MetricDefinition def)
    {
        ResolveDisplayName(def, out var icon);
        if (icon != null) return icon;
        if (def.Id == "need.hunger.avg") return TryGoodIcon("Berries");
        if (def.Id == "need.thirst.avg") return TryGoodIcon("Water");
        if (def.Id == "wellbeing.avg")   return _icons.TryGet("pop.total"); // ico-beavers
        return null;
    }

    private Sprite? TryGoodIcon(string goodId)
        => _goodService.GetGood(goodId)?.Icon.Asset;

    private string ResolveDisplayName(MetricDefinition def, out Sprite? icon)
    {
        icon = _icons.TryGet(def.Id);

        if (def.Category == MetricCategory.Goods &&
            def.Id.StartsWith("good.", StringComparison.Ordinal))
        {
            var goodId = def.Id.Substring("good.".Length);
            var spec = _goodService.GetGood(goodId);
            if (spec != null)
            {
                icon = spec.Icon.Asset;
                var display = spec.DisplayName.Value;
                if (!string.IsNullOrEmpty(display)) return TitleCase(display);
            }
            return TitleCase(goodId);
        }

        var fallback = def.NameLocKey;
        var t = _loc.T(def.NameLocKey);
        if (!string.IsNullOrEmpty(t) && t != def.NameLocKey) return TitleCase(t);

        // Strip everything before the last '.' so "Graphs.Metric.Total" → "Total".
        int lastDot = fallback.LastIndexOf('.');
        var raw = lastDot >= 0 && lastDot < fallback.Length - 1
            ? fallback.Substring(lastDot + 1)
            : fallback;
        return TitleCase(raw);
    }

    /// Uppercase the first letter of every word so labels read
    /// "Treated Planks" / "Iron Teeth" regardless of source casing.
    private static string TitleCase(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var chars = s.ToCharArray();
        bool startOfWord = true;
        for (int i = 0; i < chars.Length; i++)
        {
            char c = chars[i];
            if (char.IsWhiteSpace(c) || c == '-' || c == '/')
            {
                startOfWord = true;
                continue;
            }
            if (startOfWord && char.IsLetter(c)) chars[i] = char.ToUpperInvariant(c);
            startOfWord = false;
        }
        return new string(chars);
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Graphs.Metrics;
using UnityEngine;
using UnityEngine.UIElements;

namespace Graphs.UI;

/// Scrollable category-grouped list of per-metric checkbox rows.
/// Phase 1: curated default-visible set hard-coded.
public sealed class GraphsLegend
{
    private static readonly HashSet<string> DefaultVisible = new()
    {
        "good.Log", "good.Plank", "good.Water", "good.Berry",
        "good.MapleSyrup", "good.Gear", "good.Biofuel",
        "pop.total", "science.stored", "wellbeing.avg",
    };

    private readonly MetricRegistry _registry;

    public HashSet<string> VisibleMetricIds { get; } = new();
    public event Action? Changed;

    private readonly Dictionary<string, Label> _valueLabels = new();

    private bool _defaultsApplied;

    public GraphsLegend(MetricRegistry registry)
    {
        _registry = registry;
        // Registry is populated during Load(), which may happen AFTER this
        // constructor runs. Defer defaults until Build() is called (at which
        // point the window is being opened and the registry is fully loaded).
    }

    public VisualElement Build()
    {
        ApplyDefaultsIfNeeded();

        var scroll = new ScrollView(ScrollViewMode.Vertical);
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
        foreach (var m in _registry.Metrics)
            if (DefaultVisible.Contains(m.Id))
                VisibleMetricIds.Add(m.Id);
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
        section.style.marginBottom = 8;

        var header = new Label(category.ToString().ToUpperInvariant());
        header.style.color = new Color(0.75f, 0.75f, 0.80f);
        header.style.fontSize = 12;
        header.style.marginBottom = 2;
        section.Add(header);

        foreach (var def in metrics)
            section.Add(BuildRow(def));

        return section;
    }

    private VisualElement BuildRow(MetricDefinition def)
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.height = 22;

        var swatch = new VisualElement();
        swatch.style.width = 10; swatch.style.height = 10;
        swatch.style.marginRight = 6;
        swatch.style.backgroundColor = new StyleColor(GraphColors.ColorFor(def.Id, def.Category));
        row.Add(swatch);

        var toggle = new Toggle { value = VisibleMetricIds.Contains(def.Id) };
        toggle.style.marginRight = 4;
        toggle.RegisterValueChangedCallback(evt =>
        {
            if (evt.newValue) VisibleMetricIds.Add(def.Id);
            else VisibleMetricIds.Remove(def.Id);
            Changed?.Invoke();
        });
        row.Add(toggle);

        var name = new Label(def.NameLocKey);
        name.style.flexGrow = 1;
        name.style.color = Color.white;
        name.style.fontSize = 12;
        row.Add(name);

        var value = new Label("—");
        value.style.color = new Color(0.80f, 0.80f, 0.80f);
        value.style.fontSize = 12;
        value.style.minWidth = 52;
        value.style.unityTextAlign = TextAnchor.MiddleRight;
        _valueLabels[def.Id] = value;
        row.Add(value);

        return row;
    }
}

using Graphs.Metrics;
using Timberborn.CoreUI;
using Timberborn.RootProviders;
using UnityEngine;
using UnityEngine.UIElements;

namespace Graphs.UI;

/// Full-screen graphs window. Built on open, destroyed on close — no hidden
/// visual tree sits in the scene when closed.
public sealed class GraphsWindow
{
    private const int SortOrder = 1000;

    private readonly RootVisualElementProvider _rootProvider;
    private readonly VisualElementInitializer _elementInitializer;
    private readonly GraphsRangeSelector _rangeSelector;
    private readonly GraphsDistrictSelector _districtSelector;
    private readonly GraphsLegend _legend;
    private readonly GraphsChart _chart;
    private readonly MetricSampler _sampler;
    private readonly MetricRegistry _registry;
    private readonly DistrictFilter _filter;
    private UIDocument? _document;
    private VisualElement? _root;

    public bool IsOpen => _root != null;

    public GraphsWindow(
        RootVisualElementProvider rootProvider,
        VisualElementInitializer elementInitializer,
        GraphsRangeSelector rangeSelector,
        GraphsDistrictSelector districtSelector,
        GraphsLegend legend,
        GraphsChart chart,
        MetricSampler sampler,
        MetricRegistry registry,
        DistrictFilter filter)
    {
        _rootProvider = rootProvider;
        _elementInitializer = elementInitializer;
        _rangeSelector = rangeSelector;
        _districtSelector = districtSelector;
        _legend = legend;
        _chart = chart;
        _sampler = sampler;
        _registry = registry;
        _filter = filter;
    }

    public void Toggle()
    {
        if (IsOpen) Close();
        else Open();
    }

    public void Open()
    {
        if (IsOpen) return;

        _document = _rootProvider.CreateEmpty("graphs-window-doc", SortOrder);
        _root = Build();
        _document.rootVisualElement.Add(_root);
        // Native USS initialization deliberately skipped — LocalizableToggle
        // and friends require paired loc keys that we don't supply, and the
        // initializer was also stripping simple Image elements. Plain UIToolkit
        // widgets with our own styling behave predictably.

        _sampler.OnSampled += RefreshValues;
        _filter.Changed += _chart.Repaint;
        RefreshValues();
    }

    public void Close()
    {
        if (_root == null) return;

        _sampler.OnSampled -= RefreshValues;
        _filter.Changed -= _chart.Repaint;

        _root.RemoveFromHierarchy();
        _root = null;
        if (_document != null)
        {
            UnityEngine.Object.Destroy(_document.gameObject);
            _document = null;
        }
    }

    private void RefreshValues()
    {
        var history = _sampler.History;
        if (history.Count == 0) return;
        int last = history.Count - 1;
        _legend.UpdateCurrentValues(id =>
        {
            int idx = _registry.IndexOf(id);
            return idx < 0 ? float.NaN : history.ReadValue(last, idx);
        });
    }

    private VisualElement Build()
    {
        var backdrop = new VisualElement { name = "graphs-backdrop" };
        backdrop.style.position = Position.Absolute;
        backdrop.style.left = 0; backdrop.style.right = 0;
        backdrop.style.top = 0; backdrop.style.bottom = 0;
        backdrop.style.backgroundColor = new StyleColor(new Color(0, 0, 0, 0.55f));
        backdrop.style.justifyContent = Justify.Center;
        backdrop.style.alignItems = Align.Center;

        var panel = new VisualElement { name = "graphs-panel" };
        panel.style.width = new Length(92, LengthUnit.Percent);
        panel.style.height = new Length(88, LengthUnit.Percent);
        panel.style.maxWidth = 1800;
        panel.style.maxHeight = 1200;
        panel.style.backgroundColor = new StyleColor(new Color(0.12f, 0.11f, 0.10f, 0.97f));
        backdrop.Add(panel);

        var titleBar = new VisualElement { name = "graphs-title" };
        titleBar.style.height = 36;
        titleBar.style.flexDirection = FlexDirection.Row;
        titleBar.style.justifyContent = Justify.SpaceBetween;
        titleBar.style.alignItems = Align.Center;
        titleBar.style.paddingLeft = 12;
        titleBar.style.paddingRight = 6;
        titleBar.style.backgroundColor = new StyleColor(new Color(0.18f, 0.15f, 0.12f));

        var title = new Label("Graphs");
        title.style.color = new Color(0.96f, 0.86f, 0.62f);
        title.style.fontSize = 16;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        titleBar.Add(title);

        var closeBtn = new Button(Close) { text = "×" };
        closeBtn.style.width = 28;
        closeBtn.style.height = 28;
        closeBtn.style.fontSize = 18;
        closeBtn.style.paddingTop = 0;
        closeBtn.style.paddingBottom = 0;
        titleBar.Add(closeBtn);

        panel.Add(titleBar);

        var body = new VisualElement { name = "graphs-body" };
        body.style.flexGrow = 1;
        body.style.flexDirection = FlexDirection.Row;
        panel.Add(body);

        var chartSlot = new VisualElement { name = "graphs-chart-slot" };
        chartSlot.style.flexGrow = 1;
        chartSlot.style.marginLeft = 10;
        chartSlot.style.marginTop = 10;
        chartSlot.style.marginBottom = 10;
        chartSlot.Add(_chart.Build());
        body.Add(chartSlot);

        var legendSlot = new VisualElement { name = "graphs-legend-slot" };
        legendSlot.style.width = 320;
        legendSlot.style.marginRight = 10;
        legendSlot.style.marginTop = 10;
        legendSlot.style.marginBottom = 10;
        legendSlot.Add(_districtSelector.Build());
        legendSlot.Add(_legend.Build());
        body.Add(legendSlot);

        var bottom = new VisualElement { name = "graphs-bottom" };
        bottom.style.height = 40;
        bottom.style.flexDirection = FlexDirection.Row;
        bottom.style.justifyContent = Justify.Center;
        bottom.style.alignItems = Align.Center;
        bottom.style.backgroundColor = new StyleColor(new Color(0.15f, 0.13f, 0.11f));
        bottom.Add(_rangeSelector.Build());
        panel.Add(bottom);

        return backdrop;
    }
}

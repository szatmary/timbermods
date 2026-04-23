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
        // Apply the game's native USS styling to every element in our tree —
        // toggles pick up the game's checkbox sprite, buttons get the game's
        // nine-sliced background, scrollbars get the game's rails, etc.
        _elementInitializer.InitializeVisualElement(_root);
        // Don't call Focus() — stealing keyboard focus from the game causes
        // the game's own Esc handler (open pause menu) to stop firing after
        // the window closes. GraphsHotkey polls Esc directly instead.

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
        // Full-screen semi-transparent backdrop (like other game modals) with
        // the actual dialog panel centered inside it.
        var backdrop = new VisualElement { name = "graphs-backdrop" };
        backdrop.style.position = Position.Absolute;
        backdrop.style.left = 0; backdrop.style.right = 0;
        backdrop.style.top = 0; backdrop.style.bottom = 0;
        backdrop.style.backgroundColor = new StyleColor(new Color(0, 0, 0, 0.55f));
        backdrop.style.justifyContent = Justify.Center;
        backdrop.style.alignItems = Align.Center;

        // The dialog itself — bordered, with margin off the screen edges.
        var panel = new VisualElement { name = "graphs-panel" };
        panel.style.width = new Length(92, LengthUnit.Percent);
        panel.style.height = new Length(88, LengthUnit.Percent);
        panel.style.maxWidth = 1800;
        panel.style.maxHeight = 1200;
        panel.style.backgroundColor = new StyleColor(new Color(0.11f, 0.10f, 0.09f, 0.98f));
        panel.style.borderTopWidth = 2;
        panel.style.borderBottomWidth = 2;
        panel.style.borderLeftWidth = 2;
        panel.style.borderRightWidth = 2;
        var borderColor = new StyleColor(new Color(0.58f, 0.44f, 0.24f));
        panel.style.borderTopColor = borderColor;
        panel.style.borderBottomColor = borderColor;
        panel.style.borderLeftColor = borderColor;
        panel.style.borderRightColor = borderColor;
        panel.style.borderTopLeftRadius = 4;
        panel.style.borderTopRightRadius = 4;
        panel.style.borderBottomLeftRadius = 4;
        panel.style.borderBottomRightRadius = 4;
        backdrop.Add(panel);

        var titleBar = new VisualElement { name = "graphs-title" };
        titleBar.style.height = 40;
        titleBar.style.flexDirection = FlexDirection.Row;
        titleBar.style.justifyContent = Justify.SpaceBetween;
        titleBar.style.alignItems = Align.Center;
        titleBar.style.paddingLeft = 14;
        titleBar.style.paddingRight = 6;
        titleBar.style.backgroundColor = new StyleColor(new Color(0.18f, 0.15f, 0.12f));
        titleBar.style.borderBottomWidth = 1;
        titleBar.style.borderBottomColor = borderColor;

        var title = new Label("Graphs");
        title.style.color = new Color(0.96f, 0.86f, 0.62f);
        title.style.fontSize = 18;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        titleBar.Add(title);

        var closeBtn = NativeUi.CreateNineSliceButton("×", Close);
        closeBtn.style.width = 30;
        closeBtn.style.height = 30;
        closeBtn.style.fontSize = 20;
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
        legendSlot.style.width = 300;
        legendSlot.style.marginRight = 10;
        legendSlot.style.marginTop = 10;
        legendSlot.style.marginBottom = 10;
        legendSlot.Add(_districtSelector.Build());
        legendSlot.Add(_legend.Build());
        body.Add(legendSlot);

        var bottom = new VisualElement { name = "graphs-bottom" };
        bottom.style.height = 44;
        bottom.style.flexDirection = FlexDirection.Row;
        bottom.style.justifyContent = Justify.Center;
        bottom.style.alignItems = Align.Center;
        bottom.style.borderTopWidth = 1;
        bottom.style.borderTopColor = borderColor;
        bottom.Add(_rangeSelector.Build());
        panel.Add(bottom);

        return backdrop;
    }
}

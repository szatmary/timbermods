using Graphs.Metrics;
using Timberborn.CoreUI;
using UnityEngine;
using UnityEngine.UIElements;

namespace Graphs.UI;

/// Implements the game's canonical panel protocol (`IPanelController`) so
/// that `PanelStack.PushDialog(this)` gives us the game's modal window
/// shell — including backdrop, USS theme, Esc/Enter routing, and proper
/// focus management. We never create our own UIDocument anymore.
public sealed class GraphsWindow : IPanelController
{
    private readonly PanelStack _panelStack;
    private readonly VisualElementInitializer _elementInitializer;
    private readonly GraphsRangeSelector _rangeSelector;
    private readonly GraphsDistrictSelector _districtSelector;
    private readonly GraphsLegend _legend;
    private readonly GraphsChart _chart;
    private readonly MetricSampler _sampler;
    private readonly MetricRegistry _registry;
    private readonly DistrictFilter _filter;

    private VisualElement? _root;
    private bool _isShown;

    public bool IsOpen => _isShown;

    public GraphsWindow(
        PanelStack panelStack,
        VisualElementInitializer elementInitializer,
        GraphsRangeSelector rangeSelector,
        GraphsDistrictSelector districtSelector,
        GraphsLegend legend,
        GraphsChart chart,
        MetricSampler sampler,
        MetricRegistry registry,
        DistrictFilter filter)
    {
        _panelStack = panelStack;
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
        if (_isShown) Close();
        else Open();
    }

    public void Open()
    {
        if (_isShown) return;
        _panelStack.PushDialog(this);
        _isShown = true;

        _sampler.OnSampled += RefreshValues;
        _filter.Changed += _chart.Repaint;
        RefreshValues();
    }

    public void Close()
    {
        if (!_isShown) return;
        _sampler.OnSampled -= RefreshValues;
        _filter.Changed -= _chart.Repaint;

        _panelStack.Pop(this);
        _isShown = false;
        _root = null;
    }

    // IPanelController: the panel stack calls this to get our visual tree.
    public VisualElement GetPanel()
    {
        _root = Build();
        _elementInitializer.InitializeVisualElement(_root);
        return _root;
    }

    // IPanelController: Esc.
    public void OnUICancelled()
    {
        Close();
    }

    // IPanelController: Enter — do nothing special, but signal we handled it.
    public bool OnUIConfirmed() => false;

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
        // Chrome uses NineSliceVisualElement with the game's .sliced-border
        // classes — same pattern as Core/DialogBox.uxml. Gives us the nine-
        // sliced wooden frame background that matches other in-game dialogs.
        var panel = new NineSliceVisualElement { name = "graphs-panel" };
        panel.AddToClassList("sliced-border");
        panel.AddToClassList("sliced-border--nontransparent");
        panel.style.width = new Length(92, LengthUnit.Percent);
        panel.style.height = new Length(88, LengthUnit.Percent);
        panel.style.maxWidth = 1800;
        panel.style.maxHeight = 1200;
        panel.style.flexDirection = FlexDirection.Column;

        // Inner content wrapper — the sliced-border sprite has a thick
        // transparent frame; box__content-margin insets our content so
        // chart and legend don't overlap the frame. Matches Core/DialogBox.uxml.
        var contentMargin = new VisualElement { name = "graphs-content-margin" };
        contentMargin.AddToClassList("box__content-margin");
        contentMargin.style.flexGrow = 1;
        contentMargin.style.flexDirection = FlexDirection.Column;
        panel.Add(contentMargin);

        var titleBar = new VisualElement { name = "graphs-title" };
        titleBar.style.flexDirection = FlexDirection.Row;
        titleBar.style.justifyContent = Justify.SpaceBetween;
        titleBar.style.alignItems = Align.Center;
        titleBar.style.paddingLeft = 12;
        titleBar.style.paddingRight = 6;
        titleBar.style.height = 36;

        var title = new Label("Graphs");
        title.style.color = new Color(0.96f, 0.86f, 0.62f);
        title.style.fontSize = 16;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        titleBar.Add(title);

        var closeBtn = new NineSliceButton();
        closeBtn.AddToClassList("close-button");
        closeBtn.clicked += Close;
        titleBar.Add(closeBtn);

        contentMargin.Add(titleBar);

        var body = new VisualElement { name = "graphs-body" };
        body.style.flexGrow = 1;
        body.style.flexDirection = FlexDirection.Row;
        contentMargin.Add(body);

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
        bottom.Add(_rangeSelector.Build());
        contentMargin.Add(bottom);

        return panel;
    }
}

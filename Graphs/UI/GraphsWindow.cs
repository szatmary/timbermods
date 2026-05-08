using Graphs.Metrics;
using Timberborn.CoreUI;
using UnityEngine;
using UnityEngine.UIElements;

namespace Graphs.UI;

/// Implements `IPanelController` so the game's `PanelStack` provides our
/// window shell — backdrop, USS theme, Esc/Enter routing, focus management.
public sealed class GraphsWindow : IPanelController
{
    private readonly PanelStack _panelStack;
    private readonly VisualElementInitializer _elementInitializer;
    private readonly GraphsRangeSelector _rangeSelector;
    private readonly GraphsDistrictSelector _districtSelector;
    private readonly GraphsLegend _legend;
    private readonly GraphsChart _chart;
    private readonly MetricSampler _sampler;
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
        DistrictFilter filter)
    {
        _panelStack = panelStack;
        _elementInitializer = elementInitializer;
        _rangeSelector = rangeSelector;
        _districtSelector = districtSelector;
        _legend = legend;
        _chart = chart;
        _sampler = sampler;
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
        // Dim overlay but lockSpeed=false: the world keeps ticking while
        // the user watches the chart update live.
        _panelStack.Push(this, hideTop: false, showOverlay: true, isDialog: false, lockSpeed: false);
        _isShown = true;

        _filter.Changed += _chart.Repaint;
    }

    public void Close()
    {
        if (!_isShown) return;
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
        // The element initializer fills every LocalizableToggle's inline
        // text with its loc-key — clear those so the sibling Label is the
        // sole label source.
        _root.Query<LocalizableToggle>().ForEach(t => t.text = string.Empty);
        return _root;
    }

    // IPanelController: Esc.
    public void OnUICancelled()
    {
        Close();
    }

    // IPanelController: Enter — do nothing special, but signal we handled it.
    public bool OnUIConfirmed() => false;

    private VisualElement Build()
    {
        // Nine-sliced wooden frame — same chrome the game's own dialogs use.
        var panel = new NineSliceVisualElement { name = "graphs-panel" };
        panel.AddToClassList("sliced-border");
        panel.AddToClassList("sliced-border--nontransparent");
        panel.style.width = new Length(92, LengthUnit.Percent);
        panel.style.height = new Length(88, LengthUnit.Percent);
        panel.style.maxWidth = 1800;
        panel.style.maxHeight = 1200;
        panel.style.flexDirection = FlexDirection.Column;

        // Inset content past the ~20px sliced-border frame so chart and
        // legend don't sit under it.
        var contentMargin = new VisualElement { name = "graphs-content-margin" };
        contentMargin.AddToClassList("box__content-margin");
        contentMargin.style.paddingLeft = 20;
        contentMargin.style.paddingRight = 20;
        contentMargin.style.paddingTop = 20;
        contentMargin.style.paddingBottom = 6;
        contentMargin.style.flexGrow = 1;
        contentMargin.style.flexDirection = FlexDirection.Column;
        panel.Add(contentMargin);

        // Title bar holds the range selector centered. Fixed height keeps
        // the legend dropdown (which spawns a popup) from creeping into it.
        var titleBar = new VisualElement { name = "graphs-title" };
        titleBar.style.flexDirection = FlexDirection.Row;
        titleBar.style.justifyContent = Justify.Center;
        titleBar.style.alignItems = Align.Center;
        titleBar.style.height = 40;
        titleBar.style.flexShrink = 0;
        titleBar.style.marginBottom = 6;

        // Range selector centered. Slight marginTop so it visually sits
        // a hair below the close X.
        var rangeRow = _rangeSelector.Build();
        rangeRow.style.marginTop = 4;
        titleBar.Add(rangeRow);

        contentMargin.Add(titleBar);

        // Close X anchored to the outer panel (not contentMargin) so it
        // sits on the frame's corner, matching vanilla windows. The
        // `.close-button` USS rule handles sprite, size, and hover.
        var closeBtn = new NineSliceButton();
        closeBtn.AddToClassList("close-button");
        closeBtn.clicked += Close;
        closeBtn.style.position = Position.Absolute;
        closeBtn.style.top = 2;
        closeBtn.style.right = 2;
        panel.Add(closeBtn);

        var body = new VisualElement { name = "graphs-body" };
        body.style.flexGrow = 1;
        body.style.flexDirection = FlexDirection.Row;
        contentMargin.Add(body);

        var chartSlot = new VisualElement { name = "graphs-chart-slot" };
        chartSlot.style.flexGrow = 1;
        chartSlot.style.marginLeft = 10;
        chartSlot.style.marginTop = 10;
        chartSlot.style.marginBottom = 0;
        chartSlot.Add(_chart.Build());
        body.Add(chartSlot);

        // Legend column stretches the full body height; scroll area inside
        // the legend flex-grows so it reaches the bottom of the window.
        var legendSlot = new VisualElement { name = "graphs-legend-slot" };
        legendSlot.style.width = 230;
        legendSlot.style.marginLeft = 8;
        legendSlot.style.marginRight = 10;
        legendSlot.style.marginTop = 10;
        legendSlot.style.marginBottom = 0;
        legendSlot.style.flexDirection = FlexDirection.Column;
        legendSlot.Add(_districtSelector.Build());
        legendSlot.Add(_legend.Build());
        body.Add(legendSlot);

        return panel;
    }
}

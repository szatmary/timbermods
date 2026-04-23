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
    private readonly GraphsRangeSelector _rangeSelector;
    private readonly GraphsDistrictSelector _districtSelector;
    private readonly GraphsLegend _legend;
    private readonly GraphsChart _chart;
    private UIDocument? _document;
    private VisualElement? _root;

    public bool IsOpen => _root != null;

    public GraphsWindow(
        RootVisualElementProvider rootProvider,
        GraphsRangeSelector rangeSelector,
        GraphsDistrictSelector districtSelector,
        GraphsLegend legend,
        GraphsChart chart)
    {
        _rootProvider = rootProvider;
        _rangeSelector = rangeSelector;
        _districtSelector = districtSelector;
        _legend = legend;
        _chart = chart;
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
        _root.Focus();
    }

    public void Close()
    {
        if (_root == null) return;
        _root.RemoveFromHierarchy();
        _root = null;
        if (_document != null)
        {
            UnityEngine.Object.Destroy(_document.gameObject);
            _document = null;
        }
    }

    private VisualElement Build()
    {
        var root = new VisualElement { name = "graphs-window" };
        root.style.position = Position.Absolute;
        root.style.left = 0; root.style.right = 0; root.style.top = 0; root.style.bottom = 0;
        root.style.backgroundColor = new StyleColor(new Color(0.08f, 0.08f, 0.10f, 0.96f));
        root.focusable = true;
        root.RegisterCallback<KeyDownEvent>(e =>
        {
            if (e.keyCode == KeyCode.Escape) { Close(); e.StopPropagation(); }
        });

        var titleBar = new VisualElement { name = "graphs-title" };
        titleBar.style.height = 44;
        titleBar.style.flexDirection = FlexDirection.Row;
        titleBar.style.justifyContent = Justify.SpaceBetween;
        titleBar.style.alignItems = Align.Center;
        titleBar.style.paddingLeft = 16;
        titleBar.style.paddingRight = 8;
        titleBar.style.backgroundColor = new StyleColor(new Color(0.15f, 0.15f, 0.18f));

        var title = new Label("Graphs");
        title.style.color = Color.white;
        title.style.fontSize = 20;
        titleBar.Add(title);

        var closeBtn = new Button(Close) { text = "X" };
        closeBtn.style.width = 32; closeBtn.style.height = 32;
        titleBar.Add(closeBtn);

        root.Add(titleBar);

        var body = new VisualElement { name = "graphs-body" };
        body.style.flexGrow = 1;
        body.style.flexDirection = FlexDirection.Row;
        root.Add(body);

        var chartSlot = new VisualElement { name = "graphs-chart-slot" };
        chartSlot.style.flexGrow = 1;
        chartSlot.style.marginLeft = 12;
        chartSlot.style.marginTop = 12;
        chartSlot.style.marginBottom = 12;
        chartSlot.Add(_chart.Build());
        body.Add(chartSlot);

        var legendSlot = new VisualElement { name = "graphs-legend-slot" };
        legendSlot.style.width = 320;
        legendSlot.style.marginRight = 8;
        legendSlot.style.marginTop = 12;
        legendSlot.style.marginBottom = 12;
        legendSlot.Add(_districtSelector.Build());
        legendSlot.Add(_legend.Build());
        body.Add(legendSlot);

        var bottom = new VisualElement { name = "graphs-bottom" };
        bottom.style.height = 48;
        bottom.style.flexDirection = FlexDirection.Row;
        bottom.style.justifyContent = Justify.Center;
        bottom.style.alignItems = Align.Center;
        bottom.Add(_rangeSelector.Build());
        root.Add(bottom);

        return root;
    }
}

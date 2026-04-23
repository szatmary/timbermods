using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Graphs.UI;

public enum GraphRange { FiveDays, ThirtyDays, All }

public sealed class GraphsRangeSelector
{
    public GraphRange CurrentRange { get; private set; } = GraphRange.ThirtyDays;
    public event Action? Changed;

    private static readonly Color SelectedBg = new(0.38f, 0.30f, 0.20f);
    private static readonly Color UnselectedBg = new(0.18f, 0.16f, 0.14f);
    private static readonly Color SelectedFg = new(0.98f, 0.90f, 0.68f);
    private static readonly Color UnselectedFg = new(0.75f, 0.70f, 0.62f);
    private static readonly Color Border = new(0.42f, 0.32f, 0.20f);

    public VisualElement Build()
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;

        Button Make(string label, GraphRange range)
        {
            var btn = new Button(() => Set(range)) { text = label };
            btn.userData = range;
            StyleButton(btn, range == CurrentRange);
            return btn;
        }

        row.Add(Make("5 days", GraphRange.FiveDays));
        row.Add(Make("30 days", GraphRange.ThirtyDays));
        row.Add(Make("All", GraphRange.All));

        return row;

        void Set(GraphRange r)
        {
            if (r == CurrentRange) return;
            CurrentRange = r;
            foreach (var child in row.Children())
                StyleButton((Button)child, (GraphRange)child.userData == r);
            Changed?.Invoke();
        }
    }

    private static void StyleButton(Button btn, bool selected)
    {
        btn.style.width = 96;
        btn.style.height = 28;
        btn.style.marginLeft = 2;
        btn.style.marginRight = 2;
        btn.style.paddingTop = 0;
        btn.style.paddingBottom = 0;
        btn.style.borderTopWidth = 1;
        btn.style.borderBottomWidth = 1;
        btn.style.borderLeftWidth = 1;
        btn.style.borderRightWidth = 1;
        var border = new StyleColor(Border);
        btn.style.borderTopColor = border;
        btn.style.borderBottomColor = border;
        btn.style.borderLeftColor = border;
        btn.style.borderRightColor = border;
        btn.style.borderTopLeftRadius = 3;
        btn.style.borderTopRightRadius = 3;
        btn.style.borderBottomLeftRadius = 3;
        btn.style.borderBottomRightRadius = 3;
        btn.style.backgroundColor = new StyleColor(selected ? SelectedBg : UnselectedBg);
        btn.style.color = selected ? SelectedFg : UnselectedFg;
        btn.style.unityFontStyleAndWeight = selected ? FontStyle.Bold : FontStyle.Normal;
    }

    public float? LookbackDays() => CurrentRange switch
    {
        GraphRange.FiveDays   => 5f,
        GraphRange.ThirtyDays => 30f,
        GraphRange.All        => null,
        _                     => 30f,
    };
}

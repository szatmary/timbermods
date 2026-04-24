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
        if (!btn.ClassListContains("button-game"))
            btn.AddToClassList("button-game");

        // Game USS handles background, border, hover state. We only toggle the
        // active indicator via .button-active (a class the USS also styles).
        if (selected && !btn.ClassListContains("button-active"))
            btn.AddToClassList("button-active");
        else if (!selected && btn.ClassListContains("button-active"))
            btn.RemoveFromClassList("button-active");

        btn.style.minWidth = 96;
        btn.style.height = 32;
        btn.style.marginLeft = 2;
        btn.style.marginRight = 2;
        btn.style.paddingLeft = 8;
        btn.style.paddingRight = 8;
    }

    public float? LookbackDays() => CurrentRange switch
    {
        GraphRange.FiveDays   => 5f,
        GraphRange.ThirtyDays => 30f,
        GraphRange.All        => null,
        _                     => 30f,
    };
}

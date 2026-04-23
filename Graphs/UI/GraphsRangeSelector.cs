using System;
using UnityEngine.UIElements;

namespace Graphs.UI;

public enum GraphRange { FiveDays, ThirtyDays, All }

public sealed class GraphsRangeSelector
{
    public GraphRange CurrentRange { get; private set; } = GraphRange.ThirtyDays;
    public event Action? Changed;

    public VisualElement Build()
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;

        Button Make(string label, GraphRange range)
        {
            var btn = new Button(() => Set(range)) { text = label };
            btn.style.width = 100; btn.style.height = 36;
            btn.style.marginLeft = 6; btn.style.marginRight = 6;
            btn.userData = range;
            return btn;
        }

        row.Add(Make("5 days", GraphRange.FiveDays));
        row.Add(Make("30 days", GraphRange.ThirtyDays));
        row.Add(Make("All", GraphRange.All));

        foreach (var child in row.Children())
            ((Button)child).style.opacity = (GraphRange)child.userData == CurrentRange ? 1f : 0.6f;

        return row;

        void Set(GraphRange r)
        {
            if (r == CurrentRange) return;
            CurrentRange = r;
            foreach (var child in row.Children())
                ((Button)child).style.opacity = (GraphRange)child.userData == r ? 1f : 0.6f;
            Changed?.Invoke();
        }
    }

    public float? LookbackDays() => CurrentRange switch
    {
        GraphRange.FiveDays   => 5f,
        GraphRange.ThirtyDays => 30f,
        GraphRange.All        => null,
        _                     => 30f,
    };
}

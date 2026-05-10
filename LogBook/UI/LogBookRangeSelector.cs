using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace LogBook.UI;

public enum GraphRange { Week, Month, Year, AllTime }

public sealed class LogBookRangeSelector
{
    public GraphRange CurrentRange { get; private set; } = GraphRange.Week;
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
        // Center the button strip inside whatever parent hosts it so the
        // range selector always sits at the middle of the title bar.
        row.style.alignSelf = Align.Center;

        Button Make(string label, GraphRange range)
        {
            var btn = new Button(() => Set(range)) { text = label };
            btn.userData = range;
            StyleButton(btn, range == CurrentRange);
            return btn;
        }

        row.Add(Make("Week",     GraphRange.Week));
        row.Add(Make("Month",    GraphRange.Month));
        row.Add(Make("Year",     GraphRange.Year));
        row.Add(Make("All Time", GraphRange.AllTime));

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
        // Inline styling — `.button-game` USS doesn't apply reliably to
        // buttons hosted in the title-bar context.
        btn.style.minWidth = 96;
        btn.style.height = 28;
        btn.style.marginLeft = 2;
        btn.style.marginRight = 2;
        btn.style.paddingLeft = 8;
        btn.style.paddingRight = 8;

        btn.style.backgroundColor = selected ? SelectedBg : UnselectedBg;
        btn.style.color = selected ? SelectedFg : UnselectedFg;

        btn.style.borderTopWidth = 1;
        btn.style.borderBottomWidth = 1;
        btn.style.borderLeftWidth = 1;
        btn.style.borderRightWidth = 1;
        btn.style.borderTopColor = Border;
        btn.style.borderBottomColor = Border;
        btn.style.borderLeftColor = Border;
        btn.style.borderRightColor = Border;

        btn.style.unityFontStyleAndWeight = selected ? FontStyle.Bold : FontStyle.Normal;
        btn.style.fontSize = 12;
    }

    /// In-game day spans. Year is 13 four-week months (no leap day —
    /// it's a video game). All Time uses Old tier's full 10 000-day cap.
    public float LookbackDays() => CurrentRange switch
    {
        GraphRange.Week    => 7f,
        GraphRange.Month   => 28f,
        GraphRange.Year    => 364f,
        GraphRange.AllTime => 10000f,
        _                  => 7f,
    };
}

using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Graphs.UI;

public enum GraphRange { TenDays, HundredDays, ThousandDays, TenThousandDays }

public sealed class GraphsRangeSelector
{
    public GraphRange CurrentRange { get; private set; } = GraphRange.TenDays;
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

        row.Add(Make("10 days",    GraphRange.TenDays));
        row.Add(Make("100 days",   GraphRange.HundredDays));
        row.Add(Make("1000 days",  GraphRange.ThousandDays));
        row.Add(Make("10000 days", GraphRange.TenThousandDays));

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
        // Inline styles rather than USS classes — the game's .button-game
        // rules don't reliably apply background/text here, and the title-bar
        // context requires the buttons to paint themselves regardless.
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

    public float LookbackDays() => CurrentRange switch
    {
        GraphRange.TenDays         => 10f,
        GraphRange.HundredDays     => 100f,
        GraphRange.ThousandDays    => 1000f,
        GraphRange.TenThousandDays => 10000f,
        _                          => 10f,
    };
}

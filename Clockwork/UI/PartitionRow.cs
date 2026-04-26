using System;
using Clockwork.Services;
using UnityEngine;
using UnityEngine.UIElements;

namespace Clockwork.UI;

internal static class PartitionRow
{
    public static VisualElement Build(
        PartitionSnapshot partition,
        string displayName,
        Action<string> onRename,
        Action onToggleExpand,
        bool expanded)
    {
        var row = new VisualElement { name = "clockwork-partition-row" };
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.height = 24;
        row.style.marginBottom = 2;
        row.style.paddingLeft = 4;
        row.style.paddingRight = 4;
        row.style.backgroundColor = new StyleColor(ClockworkColors.RowBg);

        var chevron = new Button(onToggleExpand) { text = expanded ? "▾" : "▸" };
        StyleSmallButton(chevron, width: 18);
        row.Add(chevron);

        var dot = new VisualElement();
        dot.style.width = 8;
        dot.style.height = 8;
        dot.style.marginLeft = 4;
        dot.style.marginRight = 6;
        dot.style.borderTopLeftRadius = 4;
        dot.style.borderTopRightRadius = 4;
        dot.style.borderBottomLeftRadius = 4;
        dot.style.borderBottomRightRadius = 4;
        dot.style.backgroundColor = new StyleColor(
            partition.Asserting ? ClockworkColors.SignalOn : ClockworkColors.SignalOff);
        row.Add(dot);

        var label = new Label(displayName);
        label.style.color = new StyleColor(ClockworkColors.BodyText);
        label.style.flexGrow = 1;
        label.style.fontSize = 12;
        row.Add(label);

        var gear = new Button(() => StartEdit(label, row, displayName, onRename))
            { text = "⚙" };
        StyleSmallButton(gear, width: 24);
        row.Add(gear);

        return row;
    }

    private static void StyleSmallButton(Button btn, int width)
    {
        btn.style.width = width;
        btn.style.height = 20;
        btn.style.marginLeft = 2;
        btn.style.marginRight = 2;
        btn.style.fontSize = 11;
        btn.style.backgroundColor = new StyleColor(ClockworkColors.ButtonBg);
        btn.style.color = new StyleColor(ClockworkColors.BodyText);
    }

    private static void StartEdit(
        Label label, VisualElement row, string current, Action<string> onRename)
    {
        var field = new TextField { value = current };
        field.style.flexGrow = 1;
        field.style.fontSize = 12;

        int idx = row.IndexOf(label);
        row.Insert(idx, field);
        label.style.display = DisplayStyle.None;
        field.Focus();

        bool committed = false;
        void Commit()
        {
            if (committed) return;
            committed = true;
            onRename(field.value);
            field.RemoveFromHierarchy();
            label.style.display = DisplayStyle.Flex;
        }

        field.RegisterCallback<KeyDownEvent>(e =>
        {
            if (e.keyCode == KeyCode.Return) Commit();
            else if (e.keyCode == KeyCode.Escape)
            {
                committed = true;
                field.RemoveFromHierarchy();
                label.style.display = DisplayStyle.Flex;
            }
        });
        field.RegisterCallback<FocusOutEvent>(_ => Commit());
    }
}

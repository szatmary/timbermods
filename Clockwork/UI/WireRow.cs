using Clockwork.Services;
using UnityEngine;
using UnityEngine.UIElements;

namespace Clockwork.UI;

internal static class WireRow
{
    public static VisualElement Build(WireView wire, AutomatorView target)
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.marginLeft = 32;
        row.style.height = 20;

        var arrow = new Label("↳ ");
        arrow.style.color = new StyleColor(ClockworkColors.BodyText);
        arrow.style.fontSize = 11;
        row.Add(arrow);

        var label = new Label(target.DisplayName);
        label.style.color = new StyleColor(
            wire.Asserting ? ClockworkColors.SignalOn : ClockworkColors.BodyText);
        label.style.flexGrow = 1;
        label.style.fontSize = 11;
        row.Add(label);

        return row;
    }
}

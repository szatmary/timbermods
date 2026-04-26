using System.Collections.Generic;
using Clockwork.Services;
using UnityEngine;
using UnityEngine.UIElements;

namespace Clockwork.UI;

internal static class EmitterRow
{
    public static VisualElement Build(
        AutomatorView emitter,
        IEnumerable<(WireView Wire, AutomatorView Target)> downstream)
    {
        var container = new VisualElement();
        container.style.marginLeft = 16;
        container.style.flexDirection = FlexDirection.Column;

        var head = new VisualElement();
        head.style.flexDirection = FlexDirection.Row;
        head.style.alignItems = Align.Center;
        head.style.height = 22;

        var dot = new VisualElement();
        dot.style.width = 8; dot.style.height = 8; dot.style.marginRight = 6;
        dot.style.borderTopLeftRadius = 4; dot.style.borderTopRightRadius = 4;
        dot.style.borderBottomLeftRadius = 4; dot.style.borderBottomRightRadius = 4;
        dot.style.backgroundColor = new StyleColor(
            emitter.Asserting ? ClockworkColors.SignalOn : ClockworkColors.SignalOff);
        head.Add(dot);

        var name = new Label(emitter.DisplayName);
        name.style.color = new StyleColor(ClockworkColors.BodyText);
        name.style.fontSize = 11;
        name.style.flexGrow = 1;
        head.Add(name);

        container.Add(head);

        foreach (var (wire, target) in downstream)
            container.Add(WireRow.Build(wire, target));

        return container;
    }
}

using System.Collections.Generic;
using System.Linq;
using Clockwork.Services;
using UnityEngine.UIElements;

namespace Clockwork.UI;

internal static class PartitionExpandedView
{
    public static VisualElement Build(PartitionSnapshot partition)
    {
        var container = new VisualElement();
        container.style.flexDirection = FlexDirection.Column;
        container.style.marginBottom = 8;

        var byId = partition.Automators.ToDictionary(a => a.AutomatorId);

        foreach (var emitter in partition.Automators
                     .Where(a => (a.Role & AutomatorRole.Emitter) != 0))
        {
            var outgoing = new List<(WireView, AutomatorView)>();
            foreach (var wire in partition.Wires)
            {
                if (wire.FromAutomatorId != emitter.AutomatorId) continue;
                if (!byId.TryGetValue(wire.ToAutomatorId, out var target)) continue;
                outgoing.Add((wire, target));
            }
            container.Add(EmitterRow.Build(emitter, outgoing));
        }

        return container;
    }
}

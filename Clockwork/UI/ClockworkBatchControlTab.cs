using System.Collections.Generic;
using System.Linq;
using System.Text;
using Clockwork.Data;
using Clockwork.Services;
using Timberborn.BatchControl;
using Timberborn.CoreUI;
using Timberborn.EntitySystem;
using Timberborn.SingletonSystem;
using UnityEngine;
using UnityEngine.UIElements;

namespace Clockwork.UI;

/// Adds a "Clockworks" tab to the in-game Manage Settlement (Batch
/// Control) drawer. Each automation flow (vanilla AutomatorPartition) is
/// one row group; rows under it are the wires inside that flow.
public sealed class ClockworkBatchControlTab : BatchControlTab
{
    private readonly PartitionSnapshotService _snapshots;
    private readonly ClockworkRegistry _registry;
    private readonly BatchControlRowGroupFactory _rowGroupFactory;

    public ClockworkBatchControlTab(
        VisualElementLoader visualElementLoader,
        BatchControlDistrict batchControlDistrict,
        BatchControlRowGroupFactory rowGroupFactory,
        EventBus eventBus,
        PartitionSnapshotService snapshots,
        ClockworkRegistry registry)
        : base(visualElementLoader, batchControlDistrict, eventBus)
    {
        _snapshots = snapshots;
        _registry = registry;
        _rowGroupFactory = rowGroupFactory;
    }

    public override string TabNameLocKey => "Clockwork.TabName";
    public override string TabImage => "UI/Images/Game/ico-bot";  // placeholder icon
    public override string BindingKey => "Clockwork.Tab";

    /// We don't care about the per-district entity list — automation flows
    /// are settlement-wide. Setting this to true means the tab gets shown
    /// regardless of which district is selected.
    public override bool IgnoreDistrictSelection => true;

    protected override IEnumerable<BatchControlRowGroup> GetRowGroups(
        IEnumerable<EntityComponent> entities)
    {
        var snapshots = _snapshots.GetSnapshots();

        // Sort: named first (alphabetical), then unnamed by automator count desc.
        var named = new List<(PartitionSnapshot p, string name)>();
        var unnamed = new List<PartitionSnapshot>();
        foreach (var p in snapshots)
        {
            if (p.AnchorId != null && _registry.TryGet(p.AnchorId, out var n))
                named.Add((p, n));
            else
                unnamed.Add(p);
        }
        named.Sort((x, y) => System.StringComparer.OrdinalIgnoreCase.Compare(x.name, y.name));
        unnamed.Sort((x, y) => y.Automators.Count.CompareTo(x.Automators.Count));

        foreach (var (p, name) in named) yield return BuildGroup(p, name);
        foreach (var p in unnamed) yield return BuildGroup(p, DescribeFlow(p));
    }

    private BatchControlRowGroup BuildGroup(PartitionSnapshot partition, string header)
    {
        var group = _rowGroupFactory.CreateSortedWithTextHeader(header);
        // One row per wire — easiest readable layout for v1.
        var byId = partition.Automators.ToDictionary(a => a.AutomatorId);
        foreach (var wire in partition.Wires)
        {
            if (!byId.TryGetValue(wire.FromAutomatorId, out var from)) continue;
            if (!byId.TryGetValue(wire.ToAutomatorId, out var to)) continue;
            group.AddRow(BuildWireRow(from, to, wire));
        }
        return group;
    }

    private static BatchControlRow BuildWireRow(
        AutomatorView from, AutomatorView to, WireView wire)
    {
        var content = new VisualElement();
        content.style.flexDirection = FlexDirection.Row;
        content.style.alignItems = Align.Center;
        content.style.height = 22;
        content.style.paddingLeft = 8;
        content.style.paddingRight = 8;

        // Live signal dot.
        var dot = new VisualElement();
        dot.style.width = 8;
        dot.style.height = 8;
        dot.style.marginRight = 8;
        dot.style.borderTopLeftRadius = 4;
        dot.style.borderTopRightRadius = 4;
        dot.style.borderBottomLeftRadius = 4;
        dot.style.borderBottomRightRadius = 4;
        dot.style.backgroundColor = new StyleColor(
            wire.Asserting ? new Color(0.45f, 0.85f, 0.35f) : new Color(0.40f, 0.40f, 0.42f));
        content.Add(dot);

        var label = new Label($"{from.DisplayName}  →  {to.DisplayName}");
        label.style.color = new StyleColor(new Color(0.92f, 0.86f, 0.72f));
        label.style.flexGrow = 1;
        label.style.fontSize = 12;
        content.Add(label);

        // No items for v1 — just a static row.
        return new BatchControlRow(content, System.Array.Empty<IBatchControlRowItem>());
    }

    /// Build a short descriptive header for unnamed flows so the player can
    /// recognize them at a glance. e.g. `East gauge → 2 receivers`.
    private static string DescribeFlow(PartitionSnapshot partition)
    {
        // Pick the first emitter (lexicographically by id for stability).
        AutomatorView? firstEmitter = null;
        foreach (var a in partition.Automators
                     .OrderBy(a => a.AutomatorId, System.StringComparer.Ordinal))
        {
            if ((a.Role & AutomatorRole.Emitter) != 0)
            {
                firstEmitter = a;
                break;
            }
        }
        if (firstEmitter == null)
            return $"(empty flow: {partition.Automators.Count} pieces)";

        // Count distinct receivers driven by this flow.
        var receivers = new HashSet<string>();
        foreach (var w in partition.Wires) receivers.Add(w.ToAutomatorId);
        int recv = receivers.Count;
        var sb = new StringBuilder();
        sb.Append(firstEmitter.DisplayName);
        sb.Append(" → ");
        sb.Append(recv);
        sb.Append(recv == 1 ? " receiver" : " receivers");
        return sb.ToString();
    }
}

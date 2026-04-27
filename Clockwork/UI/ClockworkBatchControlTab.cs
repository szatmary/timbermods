using System;
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

/// Adds a "Clockworks" tab to the in-game Manage Settlement (Batch Control)
/// drawer. Each automation flow (vanilla AutomatorPartition) becomes one row
/// group. Inside the group: a rename row + one row per source/filter/sink.
///
/// Sources    = transmitters with no input connections (sensors, timers).
/// Filters    = automators with both inputs and outputs (gates, levers).
/// Sinks      = receivers with no output connections (floodgates, pumps).
public sealed class ClockworkBatchControlTab : BatchControlTab
{
    private readonly PartitionSnapshotService _snapshots;
    private readonly ClockworkRegistry _registry;
    private readonly BuildingPing _ping;
    private readonly BatchControlRowGroupFactory _rowGroupFactory;

    public ClockworkBatchControlTab(
        VisualElementLoader visualElementLoader,
        BatchControlDistrict batchControlDistrict,
        BatchControlRowGroupFactory rowGroupFactory,
        EventBus eventBus,
        PartitionSnapshotService snapshots,
        ClockworkRegistry registry,
        BuildingPing ping)
        : base(visualElementLoader, batchControlDistrict, eventBus)
    {
        _snapshots = snapshots;
        _registry = registry;
        _ping = ping;
        _rowGroupFactory = rowGroupFactory;
    }

    public override string TabNameLocKey => "Clockwork.TabName";
    public override string TabImage => "UI/Images/Game/ico-bot";  // placeholder — TBD
    public override string BindingKey => "Clockwork.Tab";
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
        named.Sort((x, y) => StringComparer.OrdinalIgnoreCase.Compare(x.name, y.name));
        unnamed.Sort((x, y) => y.Automators.Count.CompareTo(x.Automators.Count));

        foreach (var (p, name) in named) yield return BuildGroup(p, name);
        foreach (var p in unnamed) yield return BuildGroup(p, currentName: null);
    }

    private BatchControlRowGroup BuildGroup(PartitionSnapshot partition, string? currentName)
    {
        var (sources, filters, sinks) = ClassifyAutomators(partition);
        var summary = BuildSummary(sources, filters, sinks);
        var headerText = currentName ?? summary;
        var group = _rowGroupFactory.CreateSortedWithTextHeader(headerText);

        // Top row: a rename text field. Choosing an anchor: prefer the
        // existing one when named, else the first source (lowest-id).
        string anchorId = partition.AnchorId ?? PickAnchor(partition);
        group.AddRow(BuildRenameRow(anchorId, currentName));

        // Per-role rows. Each shows the building's name, a live state dot,
        // and a ping button to focus the camera.
        foreach (var src in sources) group.AddRow(BuildAutomatorRow(src, RoleLabel.Source));
        foreach (var flt in filters) group.AddRow(BuildAutomatorRow(flt, RoleLabel.Filter));
        foreach (var snk in sinks)   group.AddRow(BuildAutomatorRow(snk, RoleLabel.Sink));
        return group;
    }

    private enum RoleLabel { Source, Filter, Sink }

    private static string PickAnchor(PartitionSnapshot partition)
    {
        // Prefer a source. Else any automator. Lowest AutomatorId for stability.
        var ordered = partition.Automators
            .OrderBy(a => a.AutomatorId, StringComparer.Ordinal)
            .ToList();
        var firstSource = ordered.FirstOrDefault(a => (a.Role & AutomatorRole.Emitter) != 0
                                                       && !partition.Wires.Any(w => w.ToAutomatorId == a.AutomatorId));
        return firstSource?.AutomatorId ?? ordered.FirstOrDefault()?.AutomatorId ?? "";
    }

    private static (List<AutomatorView> sources, List<AutomatorView> filters, List<AutomatorView> sinks)
        ClassifyAutomators(PartitionSnapshot partition)
    {
        var hasIncoming = new HashSet<string>();
        var hasOutgoing = new HashSet<string>();
        foreach (var w in partition.Wires)
        {
            hasIncoming.Add(w.ToAutomatorId);
            hasOutgoing.Add(w.FromAutomatorId);
        }
        var sources = new List<AutomatorView>();
        var filters = new List<AutomatorView>();
        var sinks = new List<AutomatorView>();
        foreach (var a in partition.Automators)
        {
            bool inc = hasIncoming.Contains(a.AutomatorId);
            bool outg = hasOutgoing.Contains(a.AutomatorId);
            if (outg && !inc) sources.Add(a);
            else if (outg && inc) filters.Add(a);
            else if (inc && !outg) sinks.Add(a);
        }
        sources.Sort((x, y) => StringComparer.OrdinalIgnoreCase.Compare(x.DisplayName, y.DisplayName));
        filters.Sort((x, y) => StringComparer.OrdinalIgnoreCase.Compare(x.DisplayName, y.DisplayName));
        sinks.Sort((x, y) => StringComparer.OrdinalIgnoreCase.Compare(x.DisplayName, y.DisplayName));
        return (sources, filters, sinks);
    }

    private static string BuildSummary(
        List<AutomatorView> sources, List<AutomatorView> filters, List<AutomatorView> sinks)
    {
        var sb = new StringBuilder();
        sb.Append(JoinNames(sources, "(no source)"));
        if (filters.Count > 0) { sb.Append(" → "); sb.Append(JoinNames(filters, "")); }
        sb.Append(" → ");
        sb.Append(JoinNames(sinks, "(no sink)"));
        return sb.ToString();
    }

    private static string JoinNames(List<AutomatorView> list, string emptyPlaceholder)
        => list.Count == 0 ? emptyPlaceholder : string.Join(", ", list.Select(a => a.DisplayName));

    /// Top row of each group — text field for naming the flow.
    private BatchControlRow BuildRenameRow(string anchorId, string? currentName)
    {
        var content = new VisualElement();
        content.style.flexDirection = FlexDirection.Row;
        content.style.alignItems = Align.Center;
        content.style.height = 24;
        content.style.paddingLeft = 8;
        content.style.paddingRight = 8;

        var prefix = new Label("Name:");
        prefix.style.color = new StyleColor(new Color(0.85f, 0.78f, 0.62f));
        prefix.style.fontSize = 11;
        prefix.style.marginRight = 6;
        content.Add(prefix);

        var field = new TextField { value = currentName ?? "" };
        field.style.flexGrow = 1;
        field.style.fontSize = 12;
        field.RegisterCallback<KeyDownEvent>(e =>
        {
            if (e.keyCode == KeyCode.Return) Commit();
            else if (e.keyCode == KeyCode.Escape) field.value = currentName ?? "";
        });
        field.RegisterCallback<FocusOutEvent>(_ => Commit());
        content.Add(field);

        void Commit()
        {
            if (string.IsNullOrWhiteSpace(field.value))
                _registry.Remove(anchorId);
            else
                _registry.Set(anchorId, field.value.Trim());
        }

        return new BatchControlRow(content, Array.Empty<IBatchControlRowItem>());
    }

    /// Per-automator row — role tag, name, signal dot, ping button.
    private BatchControlRow BuildAutomatorRow(AutomatorView automator, RoleLabel role)
    {
        var content = new VisualElement();
        content.style.flexDirection = FlexDirection.Row;
        content.style.alignItems = Align.Center;
        content.style.height = 22;
        content.style.paddingLeft = 8;
        content.style.paddingRight = 8;

        var roleTag = new Label(role switch
        {
            RoleLabel.Source => "src",
            RoleLabel.Filter => "flt",
            _                => "out",
        });
        roleTag.style.color = new StyleColor(new Color(0.55f, 0.50f, 0.42f));
        roleTag.style.fontSize = 10;
        roleTag.style.minWidth = 28;
        roleTag.style.unityTextAlign = TextAnchor.MiddleLeft;
        content.Add(roleTag);

        var dot = new VisualElement();
        dot.style.width = 8;
        dot.style.height = 8;
        dot.style.marginRight = 8;
        dot.style.borderTopLeftRadius = 4;
        dot.style.borderTopRightRadius = 4;
        dot.style.borderBottomLeftRadius = 4;
        dot.style.borderBottomRightRadius = 4;
        dot.style.backgroundColor = new StyleColor(
            automator.Asserting ? new Color(0.45f, 0.85f, 0.35f) : new Color(0.40f, 0.40f, 0.42f));
        content.Add(dot);

        var label = new Label(automator.DisplayName);
        label.style.color = new StyleColor(new Color(0.92f, 0.86f, 0.72f));
        label.style.flexGrow = 1;
        label.style.fontSize = 12;
        content.Add(label);

        var pos = automator.WorldPosition;
        var pingBtn = new Button(() => _ping.Focus(pos)) { text = "→" };
        pingBtn.style.width = 22;
        pingBtn.style.height = 18;
        pingBtn.style.marginLeft = 4;
        pingBtn.style.fontSize = 11;
        pingBtn.style.backgroundColor = new StyleColor(new Color(0.16f, 0.14f, 0.12f));
        pingBtn.style.color = new StyleColor(new Color(0.92f, 0.86f, 0.72f));
        content.Add(pingBtn);

        return new BatchControlRow(content, Array.Empty<IBatchControlRowItem>());
    }
}

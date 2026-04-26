using System.Collections.Generic;
using Clockwork.Data;
using Timberborn.Automation;
using Timberborn.SingletonSystem;

namespace Clockwork.Services;

/// Walks the live AutomatorRegistry and produces one PartitionSnapshot per
/// connected component. The UI rebuilds its tree from this each render.
public sealed class PartitionSnapshotService : ILoadableSingleton
{
    private readonly AutomatorRegistry _automatorRegistry;
    private readonly ClockworkRegistry _clockworkRegistry;

    public PartitionSnapshotService(
        AutomatorRegistry automatorRegistry,
        ClockworkRegistry clockworkRegistry)
    {
        _automatorRegistry = automatorRegistry;
        _clockworkRegistry = clockworkRegistry;
    }

    public void Load() { /* nothing to wire up */ }

    public List<PartitionSnapshot> GetSnapshots()
    {
        // Group automators by partition. AutomatorPartition objects are
        // distinct instances per connected component — reference equality
        // suffices. Automators not yet in any partition are skipped.
        var byPartition = new Dictionary<AutomatorPartition, PartitionSnapshot>();
        foreach (var automator in _automatorRegistry.Automators)
        {
            var partition = automator.Partition;
            if (partition == null) continue;
            if (!byPartition.TryGetValue(partition, out var snap))
            {
                snap = new PartitionSnapshot();
                byPartition[partition] = snap;
            }
            snap.Automators.Add(BuildAutomatorView(automator));
            // Wires are built from each transmitter's outgoing connections.
            foreach (var conn in automator.OutputConnections)
            {
                if (conn.Receiver == null) continue;
                snap.Wires.Add(new WireView
                {
                    FromAutomatorId = automator.AutomatorId,
                    ToAutomatorId = conn.Receiver.AutomatorId,
                    Asserting = conn.BooleanState,
                });
                if (conn.BooleanState) snap.Asserting = true;
            }
        }
        // Resolve AnchorId per partition.
        foreach (var snap in byPartition.Values)
        {
            foreach (var a in snap.Automators)
            {
                if (_clockworkRegistry.TryGet(a.AutomatorId, out _))
                {
                    snap.AnchorId = a.AutomatorId;
                    break;
                }
            }
        }
        return new List<PartitionSnapshot>(byPartition.Values);
    }

    private static AutomatorView BuildAutomatorView(Automator automator)
    {
        var role = AutomatorRole.None;
        if (automator.IsTransmitter) role |= AutomatorRole.Emitter;
        if (automator.HasComponent<IAutomatableNeeder>()) role |= AutomatorRole.Receiver;
        bool asserting = false;
        // For transmitters, an outgoing connection's BooleanState tells us
        // whether the transmitter is currently asserting.
        foreach (var conn in automator.OutputConnections)
        {
            if (conn.BooleanState) { asserting = true; break; }
        }
        return new AutomatorView
        {
            AutomatorId = automator.AutomatorId,
            DisplayName = string.IsNullOrEmpty(automator.AutomatorName)
                ? automator.Name
                : automator.AutomatorName,
            Role = role,
            Asserting = asserting,
            WorldPosition = automator.Transform != null
                ? automator.Transform.position
                : UnityEngine.Vector3.zero,
        };
    }
}

using System.Reflection;
using HarmonyLib;
using Timberborn.MechanicalSystem;
using Timberborn.ZiplineSystem;
namespace AdvancedZipLineStation;

/// <summary>
/// Harmony patches that bridge the zipline and mechanical power systems.
/// When two stations with MechanicalNodes are connected via zipline cable,
/// their mechanical graphs are merged so power flows through the connection.
///
/// Graph merging (Join) keeps nodes in the same power graph.
/// A postfix on MechanicalGraphIterator.Iterate extends the UI highlighting
/// to include bridged nodes that aren't physically connected via transputs.
/// </summary>
[HarmonyPatch]
public static class ZiplinePowerTransferPatch
{
    // Track active zipline power bridges (pairs of MechanicalNodes)
    private static readonly HashSet<(MechanicalNode, MechanicalNode)> _bridges = new();
    private static bool _hasPendingMerges;

    // Cached reflection for accessing internal game types
    private static readonly FieldInfo? GraphManagerField = typeof(MechanicalNode).GetField(
        "_mechanicalGraphManager", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly Type? GraphManagerType = GraphManagerField?.FieldType;
    private static readonly FieldInfo? FactoryField = GraphManagerType?.GetField(
        "_mechanicalGraphFactory", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly MethodInfo? JoinMethod = FactoryField?.FieldType.GetMethod(
        "Join", BindingFlags.Public | BindingFlags.Instance);

    /// <summary>
    /// After a zipline connection is established (both gameplay and save-load restore).
    /// No IsActive guard — on load, Connect fires from PostInitializeEntity before
    /// OnEnterFinishedState, so we track the bridge early and merge when graphs exist.
    /// </summary>
    [HarmonyPatch(typeof(ZiplineConnectionService), nameof(ZiplineConnectionService.Connect))]
    [HarmonyPostfix]
    public static void ConnectPostfix(ZiplineTower ziplineTower, ZiplineTower otherZiplineTower)
    {
        TryBridge(ziplineTower, otherZiplineTower);
    }

    /// <summary>
    /// After a zipline connection is activated (tower finished construction).
    /// </summary>
    [HarmonyPatch(typeof(ZiplineConnectionService), nameof(ZiplineConnectionService.ActivateConnection))]
    [HarmonyPostfix]
    public static void ActivateConnectionPostfix(ZiplineTower ziplineTower, ZiplineTower otherZiplineTower)
    {
        TryBridge(ziplineTower, otherZiplineTower);
    }

    /// <summary>
    /// When a tower enters finished state, bridge all its current connections.
    /// This is the key hook for save-load: by this point MechanicalNode graphs
    /// are initialized and ConnectionTargets are populated from saved data.
    /// Uses TargetMethod to safely resolve the method (may be explicit interface impl).
    /// </summary>
    [HarmonyPatch]
    public static class OnEnterFinishedStatePatch
    {
        static MethodBase? TargetMethod()
        {
            return typeof(ZiplineTower).GetMethod("OnEnterFinishedState",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        }

        [HarmonyPostfix]
        public static void Postfix(ZiplineTower __instance)
        {
            var nodeA = __instance.GetComponent<MechanicalNode>();
            if (nodeA == null) return;

            foreach (var target in __instance.ConnectionTargets)
            {
                if (target == null) continue;
                var nodeB = target.GetComponent<MechanicalNode>();
                if (nodeB == null) continue;

                if (!_bridges.Contains((nodeA, nodeB)) && !_bridges.Contains((nodeB, nodeA)))
                    _bridges.Add((nodeA, nodeB));

                MergeGraphs(nodeA, nodeB);
            }
        }
    }

    /// <summary>
    /// Before a zipline connection is disconnected, remove our bridge tracking.
    /// The next graph reorganization will naturally split the graphs.
    /// </summary>
    [HarmonyPatch(typeof(ZiplineConnectionService), nameof(ZiplineConnectionService.Disconnect))]
    [HarmonyPrefix]
    public static void DisconnectPrefix(ZiplineTower ziplineTower, ZiplineTower otherZiplineTower)
    {
        var nodeA = ziplineTower.GetComponent<MechanicalNode>();
        var nodeB = otherZiplineTower.GetComponent<MechanicalNode>();
        if (nodeA == null || nodeB == null)
            return;

        _bridges.Remove((nodeA, nodeB));
        _bridges.Remove((nodeB, nodeA));

        // Trigger reorganization to split the graphs
        var graph = nodeA.Graph;
        if (graph != null)
            InvokeReorganize(nodeA, graph);
    }

    /// <summary>
    /// After any graph reorganization, re-merge graphs for our tracked bridges.
    /// This keeps zipline-bridged stations in the same graph even after the
    /// reorganizer splits them due to lack of physical transput connections.
    /// </summary>
    [HarmonyPatch]
    public static class ReorganizePostfixPatch
    {
        static MethodBase? TargetMethod()
        {
            var reorganizerType = GraphManagerType?.GetField(
                "_mechanicalGraphReorganizer", BindingFlags.NonPublic | BindingFlags.Instance)?.FieldType;
            return reorganizerType?.GetMethod("Reorganize", BindingFlags.Public | BindingFlags.Instance);
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            RemergeAllBridges();
        }
    }

    /// <summary>
    /// After the UI graph iterator finishes traversing transputs, inject our
    /// bridged nodes so they appear in the network highlight when selected.
    /// The iterator walks physical transput connections via DFS but can't see
    /// our virtual zipline bridges, so we expand the result set.
    /// </summary>
    [HarmonyPatch]
    public static class GraphIteratorPostfixPatch
    {
        static MethodBase? TargetMethod()
        {
            var iteratorType = typeof(MechanicalNode).Assembly
                .GetType("Timberborn.MechanicalSystemHighlighting.MechanicalGraphIterator")
                ?? AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetType("Timberborn.MechanicalSystemHighlighting.MechanicalGraphIterator"))
                    .FirstOrDefault(t => t != null);
            return iteratorType?.GetMethod("Iterate", BindingFlags.Public | BindingFlags.Instance);
        }

        [HarmonyPostfix]
        public static void Postfix(ICollection<MechanicalNode> graphNodes)
        {
            ExpandWithBridgedNodes(graphNodes);
        }
    }

    /// <summary>
    /// The transput DFS only finds physically adjacent nodes. Since our merged
    /// graph contains nodes from both sides of zipline bridges, we expand the
    /// result to include ALL nodes in the graph — giving full network highlighting.
    /// </summary>
    private static void ExpandWithBridgedNodes(ICollection<MechanicalNode> graphNodes)
    {
        if (_bridges.Count == 0 || graphNodes.Count == 0) return;

        // Collect graphs that have bridges
        var bridgedGraphs = new HashSet<MechanicalGraph>();
        foreach (var (a, b) in _bridges)
        {
            if (a?.Graph != null) bridgedGraphs.Add(a.Graph);
            if (b?.Graph != null) bridgedGraphs.Add(b.Graph);
        }

        if (bridgedGraphs.Count == 0) return;

        // If any node from the DFS result is in a bridged graph, add all
        // nodes from that graph (includes both sides of the zipline)
        MechanicalGraph? targetGraph = null;
        foreach (var node in graphNodes)
        {
            if (node?.Graph != null && bridgedGraphs.Contains(node.Graph))
            {
                targetGraph = node.Graph;
                break;
            }
        }

        if (targetGraph == null) return;

        var nodesToAdd = new List<MechanicalNode>();
        foreach (var node in targetGraph.Nodes)
        {
            if (!graphNodes.Contains(node))
                nodesToAdd.Add(node);
        }
        foreach (var node in nodesToAdd)
            graphNodes.Add(node);
    }

    private static void TryBridge(ZiplineTower towerA, ZiplineTower towerB)
    {
        var nodeA = towerA.GetComponent<MechanicalNode>();
        var nodeB = towerB.GetComponent<MechanicalNode>();
        if (nodeA == null || nodeB == null)
            return;

        // Track the bridge (avoid duplicates in both orderings)
        if (!_bridges.Contains((nodeA, nodeB)) && !_bridges.Contains((nodeB, nodeA)))
            _bridges.Add((nodeA, nodeB));

        // Always attempt merge — a previous call may have failed due to null graphs
        // during early initialization. MergeGraphs is safe to call repeatedly.
        MergeGraphs(nodeA, nodeB);
    }

    private static void MergeGraphs(MechanicalNode nodeA, MechanicalNode nodeB)
    {
        var graphA = nodeA.Graph;
        var graphB = nodeB.Graph;
        if (graphA != null && graphB != null && graphA != graphB)
            InvokeJoin(nodeA, graphA, graphB);
        else if (graphA == null || graphB == null)
            _hasPendingMerges = true;
    }

    /// <summary>
    /// Called by ZiplinePowerMergeRetrier after load and on each tick.
    /// Retries merges that failed due to null graphs during initialization.
    /// </summary>
    public static void RetryPendingMerges()
    {
        if (!_hasPendingMerges) return;
        _hasPendingMerges = false;
        RemergeAllBridges();
    }

    /// <summary>
    /// Re-merge all tracked bridges. Called after graph reorganization.
    /// </summary>
    private static void RemergeAllBridges()
    {
        // Clean up bridges to destroyed buildings
        _bridges.RemoveWhere(b => b.Item1 == null || b.Item2 == null);

        foreach (var (nodeA, nodeB) in _bridges)
        {
            var graphA = nodeA.Graph;
            var graphB = nodeB.Graph;
            if (graphA != null && graphB != null && graphA != graphB)
            {
                InvokeJoin(nodeA, graphA, graphB);
            }
        }
    }

    private static void InvokeReorganize(MechanicalNode node, MechanicalGraph graph)
    {
        var manager = GraphManagerField?.GetValue(node);
        if (manager == null) return;
        var reorganizerField = GraphManagerType?.GetField(
            "_mechanicalGraphReorganizer", BindingFlags.NonPublic | BindingFlags.Instance);
        var reorganizer = reorganizerField?.GetValue(manager);
        var reorganizeMethod = reorganizer?.GetType().GetMethod(
            "Reorganize", BindingFlags.Public | BindingFlags.Instance);
        reorganizeMethod?.Invoke(reorganizer, new object[] { graph });
    }

    private static void InvokeJoin(MechanicalNode node, MechanicalGraph graphA, MechanicalGraph graphB)
    {
        var manager = GraphManagerField?.GetValue(node);
        if (manager == null) return;
        var factory = FactoryField?.GetValue(manager);
        if (factory == null) return;
        JoinMethod?.Invoke(factory, new object[] { new[] { graphA, graphB } });
    }
}

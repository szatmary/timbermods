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
/// Instead of connecting transputs (which breaks the UI graph traversal),
/// we directly merge graphs via Join() and track our virtual connections
/// so we can re-merge after any graph reorganization.
/// </summary>
[HarmonyPatch]
public static class ZiplinePowerTransferPatch
{
    // Track active zipline power bridges (pairs of MechanicalNodes)
    private static readonly HashSet<(MechanicalNode, MechanicalNode)> _bridges = new();

    // Cached reflection for accessing internal game types
    private static readonly FieldInfo? GraphManagerField = typeof(MechanicalNode).GetField(
        "_mechanicalGraphManager", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly Type? GraphManagerType = GraphManagerField?.FieldType;
    private static readonly FieldInfo? FactoryField = GraphManagerType?.GetField(
        "_mechanicalGraphFactory", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly MethodInfo? JoinMethod = FactoryField?.FieldType.GetMethod(
        "Join", BindingFlags.Public | BindingFlags.Instance);

    /// <summary>
    /// After a new zipline connection is established between active towers.
    /// </summary>
    [HarmonyPatch(typeof(ZiplineConnectionService), nameof(ZiplineConnectionService.Connect))]
    [HarmonyPostfix]
    public static void ConnectPostfix(ZiplineTower ziplineTower, ZiplineTower otherZiplineTower)
    {
        if (ziplineTower.IsActive && otherZiplineTower.IsActive)
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

    private static void TryBridge(ZiplineTower towerA, ZiplineTower towerB)
    {
        var nodeA = towerA.GetComponent<MechanicalNode>();
        var nodeB = towerB.GetComponent<MechanicalNode>();
        if (nodeA == null || nodeB == null)
            return;

        // Already tracked
        if (_bridges.Contains((nodeA, nodeB)))
            return;

        _bridges.Add((nodeA, nodeB));

        MergeGraphs(nodeA, nodeB);
    }

    private static void MergeGraphs(MechanicalNode nodeA, MechanicalNode nodeB)
    {
        var graphA = nodeA.Graph;
        var graphB = nodeB.Graph;
        if (graphA != null && graphB != null && graphA != graphB)
        {
            InvokeJoin(nodeA, graphA, graphB);
        }
    }

    /// <summary>
    /// Re-merge all tracked bridges. Called after graph reorganization.
    /// </summary>
    private static void RemergeAllBridges()
    {
        foreach (var (nodeA, nodeB) in _bridges)
        {
            if (nodeA == null || nodeB == null)
                continue;
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

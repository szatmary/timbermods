using System.Reflection;
using HarmonyLib;
using Timberborn.MechanicalSystem;
using Timberborn.ZiplineSystem;

namespace AdvancedZipLineStation;

/// <summary>
/// Harmony patches that bridge the zipline and mechanical power systems.
/// When two AdvancedZipLineStations are connected via zipline cable, their
/// mechanical nodes are linked so power transfers through the connection.
/// </summary>
[HarmonyPatch]
public static class ZiplinePowerTransferPatch
{
    // Cached reflection for accessing internal game types
    private static readonly FieldInfo? GraphManagerField = typeof(MechanicalNode).GetField(
        "_mechanicalGraphManager", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly Type? GraphManagerType = GraphManagerField?.FieldType;
    private static readonly FieldInfo? ReorganizerField = GraphManagerType?.GetField(
        "_mechanicalGraphReorganizer", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? FactoryField = GraphManagerType?.GetField(
        "_mechanicalGraphFactory", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly MethodInfo? ReorganizeMethod = ReorganizerField?.FieldType.GetMethod(
        "Reorganize", BindingFlags.Public | BindingFlags.Instance);
    private static readonly MethodInfo? JoinMethod = FactoryField?.FieldType.GetMethod(
        "Join", BindingFlags.Public | BindingFlags.Instance);

    /// <summary>
    /// After a new zipline connection is established, link the mechanical nodes
    /// if both towers are already active (finished construction).
    /// </summary>
    [HarmonyPatch(typeof(ZiplineConnectionService), nameof(ZiplineConnectionService.Connect))]
    [HarmonyPostfix]
    public static void ConnectPostfix(ZiplineTower ziplineTower, ZiplineTower otherZiplineTower)
    {
        if (ziplineTower.IsActive && otherZiplineTower.IsActive)
            TryConnectMechanicalNodes(ziplineTower, otherZiplineTower);
    }

    /// <summary>
    /// After a zipline connection is activated (tower finished construction),
    /// link the mechanical nodes so power flows through the cable.
    /// </summary>
    [HarmonyPatch(typeof(ZiplineConnectionService), nameof(ZiplineConnectionService.ActivateConnection))]
    [HarmonyPostfix]
    public static void ActivateConnectionPostfix(ZiplineTower ziplineTower, ZiplineTower otherZiplineTower)
    {
        TryConnectMechanicalNodes(ziplineTower, otherZiplineTower);
    }

    /// <summary>
    /// Before a zipline connection is disconnected, unlink the mechanical
    /// transput pair that bridges the two towers.
    /// </summary>
    [HarmonyPatch(typeof(ZiplineConnectionService), nameof(ZiplineConnectionService.Disconnect))]
    [HarmonyPrefix]
    public static void DisconnectPrefix(ZiplineTower ziplineTower, ZiplineTower otherZiplineTower)
    {
        var nodeA = ziplineTower.GetComponent<MechanicalNode>();
        var nodeB = otherZiplineTower.GetComponent<MechanicalNode>();
        if (nodeA == null || nodeB == null)
            return;

        var linkedTransput = FindTransputConnectedTo(nodeA, nodeB);
        if (linkedTransput == null)
            return;

        var graph = nodeA.Graph;
        linkedTransput.Disconnect();
        if (graph != null)
            InvokeReorganize(nodeA, graph);
    }

    private static void TryConnectMechanicalNodes(ZiplineTower towerA, ZiplineTower towerB)
    {
        var nodeA = towerA.GetComponent<MechanicalNode>();
        var nodeB = towerB.GetComponent<MechanicalNode>();
        if (nodeA == null || nodeB == null)
            return;

        // Already connected via zipline — skip (idempotent)
        if (FindTransputConnectedTo(nodeA, nodeB) != null)
            return;

        var transputA = FindFreeTransput(nodeA);
        var transputB = FindFreeTransput(nodeB);
        if (transputA == null || transputB == null)
            return;

        // Link the transputs so the mechanical graph flood-fill traverses them
        transputA.Connect(transputB);

        // Merge the two separate mechanical graphs into one
        var graphA = nodeA.Graph;
        var graphB = nodeB.Graph;
        if (graphA != null && graphB != null && graphA != graphB)
        {
            InvokeJoin(nodeA, graphA, graphB);
        }
        else if (graphA != null)
        {
            InvokeReorganize(nodeA, graphA);
        }
    }

    private static Transput? FindFreeTransput(MechanicalNode node)
    {
        foreach (var transput in node.Transputs)
        {
            if (!transput.Connected)
                return transput;
        }
        return null;
    }

    private static Transput? FindTransputConnectedTo(MechanicalNode nodeA, MechanicalNode nodeB)
    {
        foreach (var transput in nodeA.Transputs)
        {
            if (transput.Connected && transput.ConnectedTransput?.ParentNode == nodeB)
                return transput;
        }
        return null;
    }

    private static void InvokeReorganize(MechanicalNode node, MechanicalGraph graph)
    {
        var manager = GraphManagerField?.GetValue(node);
        if (manager == null) return;
        var reorganizer = ReorganizerField?.GetValue(manager);
        if (reorganizer == null) return;
        ReorganizeMethod?.Invoke(reorganizer, new object[] { graph });
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

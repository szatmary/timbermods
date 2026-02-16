using System.Reflection;
using HarmonyLib;
using Timberborn.BlockSystem;
using Timberborn.Explosions;
using UnityEngine;

namespace DynamiteRubble;

[HarmonyPatch]
public static class DynamiteDetonatePatch
{
    private static readonly FieldInfo? TerrainServiceField =
        typeof(Dynamite).GetField("_terrainService",
            BindingFlags.NonPublic | BindingFlags.Instance);

    private static MethodInfo? _getDestroyedLayersCountMethod;

    [ThreadStatic]
    private static int _layersDestroyed;
    [ThreadStatic]
    private static Vector3Int _coords;

    static MethodBase? TargetMethod()
    {
        return typeof(Dynamite).GetMethod("Detonate",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
    }

    [HarmonyPrefix]
    public static void Prefix(Dynamite __instance)
    {
        _layersDestroyed = 0;
        try
        {
            var blockObject = __instance.GetComponent<BlockObject>();
            if (blockObject == null) return;

            _coords = blockObject.Coordinates;
            var belowCoords = new Vector3Int(_coords.x, _coords.y, _coords.z - 1);

            var terrainService = TerrainServiceField?.GetValue(__instance);
            if (terrainService == null) return;

            if (_getDestroyedLayersCountMethod == null)
            {
                _getDestroyedLayersCountMethod = terrainService.GetType().GetMethod(
                    "GetDestroyedLayersCount",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            }
            if (_getDestroyedLayersCountMethod == null) return;

            int depth = __instance.Depth;

            // Clamp depth so GetDestroyedLayersCount never scans below z=0.
            // The game's IsTerrainVoxel returns true for negative z coords
            // (array index wraparound), causing false counts.
            // belowCoords.z + 1 = number of z-levels from belowCoords.z down to z=0.
            int clampedDepth = Math.Min(depth, belowCoords.z + 1);
            if (clampedDepth <= 0) return;

            _layersDestroyed = (int)_getDestroyedLayersCountMethod.Invoke(
                terrainService, new object[] { belowCoords, clampedDepth })!;


        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[DynamiteRubble] Prefix error: {ex}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(Dynamite __instance)
    {
        if (_layersDestroyed <= 0) return;
        try
        {
            int amount = _layersDestroyed * 3;
            DynamiteRubbleService.Instance?.SpawnDirt(_coords, amount);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[DynamiteRubble] Postfix error: {ex.Message}");
        }
    }
}

[HarmonyPatch]
public static class TunnelExplodePatch
{
    private static readonly FieldInfo? BlockObjectField =
        typeof(Dynamite).Assembly
            .GetType("Timberborn.Explosions.Tunnel")?
            .GetField("_blockObject", BindingFlags.NonPublic | BindingFlags.Instance);

    static MethodBase? TargetMethod()
    {
        return typeof(Dynamite).Assembly
            .GetType("Timberborn.Explosions.Tunnel")?
            .GetMethod("Explode",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
    }

    [HarmonyPostfix]
    public static void Postfix(object __instance)
    {
        try
        {
            var blockObject = BlockObjectField?.GetValue(__instance) as BlockObject;
            if (blockObject == null) return;

            DynamiteRubbleService.Instance?.SpawnDirt(blockObject.Coordinates, 3);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[DynamiteRubble] Tunnel error: {ex.Message}");
        }
    }
}

[HarmonyPatch]
public static class SpawnerCapturePatch
{
    static MethodBase? TargetMethod()
    {
        var type = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType("Timberborn.RecoveredGoodSystem.BuildingGoodsRecoveryService"))
            .FirstOrDefault(t => t != null);
        return type?.GetConstructors().FirstOrDefault();
    }

    [HarmonyPostfix]
    public static void Postfix(object __instance)
    {
        var field = __instance.GetType().GetField("_recoveredGoodStackSpawner",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var spawner = field?.GetValue(__instance);
        if (spawner != null)
            DynamiteRubbleService.CapturedSpawner = spawner;
    }
}

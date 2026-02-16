using System.Reflection;
using Timberborn.Goods;
using Timberborn.SingletonSystem;
using UnityEngine;

namespace DynamiteRubble;

public class DynamiteRubbleService : IPostLoadableSingleton
{
    public static DynamiteRubbleService? Instance { get; private set; }
    internal static object? CapturedSpawner;

    private MethodInfo? _addAwaitingGoodsMethod;
    private bool _methodsResolved;

    public void PostLoad()
    {
        Instance = this;
        TryResolveMethods();
    }

    private void TryResolveMethods()
    {
        if (_methodsResolved || CapturedSpawner == null) return;

        var spawnerType = CapturedSpawner.GetType();
        _addAwaitingGoodsMethod = spawnerType.GetMethod(
            "AddAwaitingGoods",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        _methodsResolved = true;

        if (_addAwaitingGoodsMethod == null)
            Debug.LogWarning("[DynamiteRubble] Could not find AddAwaitingGoods");
    }

    public void SpawnDirt(Vector3Int coordinates, int amount)
    {
        TryResolveMethods();

        if (CapturedSpawner == null)
        {
            Debug.LogWarning("[DynamiteRubble] No captured spawner");
            return;
        }
        if (_addAwaitingGoodsMethod == null)
        {
            Debug.LogWarning("[DynamiteRubble] AddAwaitingGoods not found");
            return;
        }

        try
        {
            var goodAmounts = new[] { new GoodAmount("Dirt", amount) };
            _addAwaitingGoodsMethod.Invoke(CapturedSpawner,
                new object[] { coordinates, (IEnumerable<GoodAmount>)goodAmounts });
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[DynamiteRubble] SpawnDirt failed: {ex}");
        }
    }
}

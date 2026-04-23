using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Timberborn.GameDistricts;
using Timberborn.Goods;
using Timberborn.InventorySystem;
using UnityEngine;

namespace Graphs.Metrics.Providers;

/// Registers one metric per good id discovered at load. Values are summed stock
/// totals across every inventory (settlement-wide) or within the currently
/// selected district (when the filter names one).
///
/// Good enumeration: `IGoodService.Goods` returns a `ReadOnlyList<string>` of good
/// ids — the service already exposes them as strings, no need to unwrap specs.
/// Per-inventory stock: `Inventory.AmountInStock(goodId)` returns the current int
/// total for that good (respects the inventory's allowed-goods set).
/// Inventory -> district: `inventory.GetComponent<DistrictBuilding>().District`
/// gives the owning `DistrictCenter`. Inventories without a DistrictBuilding
/// (or assigned to no district) are treated as unassigned and skipped when the
/// filter is narrowed to a specific district.
public sealed class GoodsMetricProvider : IMetricProvider
{
    private readonly IGoodService _goodService;
    private readonly DistrictInventoryRegistry _districtInventoryRegistry;

    public GoodsMetricProvider(
        IGoodService goodService,
        DistrictInventoryRegistry districtInventoryRegistry)
    {
        _goodService = goodService;
        _districtInventoryRegistry = districtInventoryRegistry;
    }

    public IEnumerable<MetricDefinition> GetMetrics()
    {
        var goodIds = _goodService.Goods
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var goodId in goodIds)
        {
            // Capture for closure — loop variable reuse would alias all metrics
            // to the last id otherwise.
            var captured = goodId;
            yield return new MetricDefinition(
                id: $"good.{captured}",
                nameLocKey: $"Good.{captured}",
                category: MetricCategory.Goods,
                scope: MetricScope.Either,
                valueFn: districtName => TotalStock(captured, districtName));
        }
    }

    private static bool _loggedStockDiagnostic;
    private static bool _loggedInventoryCount;

    // Reflection-resolved access to the game-internal InventoryRegistry,
    // which is the settlement-wide inventory set (DistrictInventoryRegistry
    // is a wrapper whose `Inventories` getter throws NRE in our context).
    private static FieldInfo? _publicInventoryRegistryField;
    private static PropertyInfo? _innerInventoriesProp;
    private static bool _reflectionResolved;

    private IEnumerable<Inventory> EnumerateInventories()
    {
        if (!_reflectionResolved)
        {
            _reflectionResolved = true;
            try
            {
                var regType = _districtInventoryRegistry.GetType();
                _publicInventoryRegistryField = regType.GetField(
                    "_publicInventoryRegistry",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var innerType = _publicInventoryRegistryField?.FieldType
                    ?? typeof(Inventory).Assembly.GetType("Timberborn.InventorySystem.InventoryRegistry");
                _innerInventoriesProp = innerType?.GetProperty(
                    "Inventories",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    ?? innerType?.GetProperty(
                        "AllInventories",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    ?? innerType?.GetProperty(
                        "EnabledInventories",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                Debug.Log(
                    $"[Graphs] reflection: publicInventoryRegistryField={_publicInventoryRegistryField != null} "
                    + $"innerProp={_innerInventoriesProp?.Name}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Graphs] reflection setup failed: {ex.Message}");
            }
        }

        if (_publicInventoryRegistryField == null || _innerInventoriesProp == null)
            return Enumerable.Empty<Inventory>();

        var inner = _publicInventoryRegistryField.GetValue(_districtInventoryRegistry);
        if (inner == null) return Enumerable.Empty<Inventory>();

        if (_innerInventoriesProp.GetValue(inner) is IEnumerable<Inventory> typed)
            return typed;
        if (_innerInventoriesProp.GetValue(inner) is System.Collections.IEnumerable raw)
            return raw.Cast<Inventory>();

        return Enumerable.Empty<Inventory>();
    }

    /// Sum `goodId` stock across inventories that pass the district filter.
    /// A null `districtName` means settlement-wide: every inventory counts.
    private float TotalStock(string goodId, string? districtName)
    {
        var total = 0;
        var anyInventory = false;
        try
        {
            foreach (var inventory in EnumerateInventories())
            {
                if (inventory == null) continue;
                anyInventory = true;
                try
                {
                    if (districtName != null && GetDistrictName(inventory) != districtName)
                    {
                        continue;
                    }
                    total += inventory.AmountInStock(goodId);
                }
                catch (Exception ex)
                {
                    if (!_loggedStockDiagnostic)
                    {
                        _loggedStockDiagnostic = true;
                        Debug.LogWarning($"[Graphs] inventory stock probe failed for '{goodId}': {ex}\n{ex.StackTrace}");
                    }
                }
            }
            if (!_loggedInventoryCount && goodId == "Log")
            {
                _loggedInventoryCount = true;
                int count = 0, nonEmpty = 0;
                foreach (var inv in EnumerateInventories())
                {
                    count++;
                    try { if (inv.AmountInStock("Log") > 0) nonEmpty++; } catch { }
                }
                Debug.Log($"[Graphs] goods diag: {count} inventories via reflection, {nonEmpty} hold Log; Log sum={total}");
            }
            return anyInventory ? total : float.NaN;
        }
        catch (Exception ex)
        {
            if (!_loggedStockDiagnostic)
            {
                _loggedStockDiagnostic = true;
                Debug.LogWarning($"[Graphs] goods enumeration failed: {ex}\n{ex.StackTrace}");
            }
            return anyInventory ? total : float.NaN;
        }
    }

    /// Resolve an inventory's owning district name, or null if it isn't assigned
    /// to one (e.g. stockpiles with no district center yet).
    private static string? GetDistrictName(Inventory inventory)
    {
        var districtBuilding = inventory.GetComponent<DistrictBuilding>();
        return districtBuilding?.District?.DistrictName;
    }
}

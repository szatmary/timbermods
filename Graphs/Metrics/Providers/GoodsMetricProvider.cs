using System;
using System.Collections.Generic;
using System.Linq;
using Timberborn.GameDistricts;
using Timberborn.Goods;
using Timberborn.InventorySystem;

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

    /// Sum `goodId` stock across inventories that pass the district filter.
    /// A null `districtName` means settlement-wide: every inventory counts.
    /// Exceptions from a single inventory are swallowed so one broken component
    /// can't zero the whole total.
    private float TotalStock(string goodId, string? districtName)
    {
        var total = 0;
        foreach (var inventory in _districtInventoryRegistry.Inventories)
        {
            try
            {
                if (districtName != null && GetDistrictName(inventory) != districtName)
                {
                    continue;
                }
                total += inventory.AmountInStock(goodId);
            }
            catch
            {
                // Skip inventories that can't answer — don't let one bad component
                // mask the rest of the settlement's stock.
            }
        }
        return total;
    }

    /// Resolve an inventory's owning district name, or null if it isn't assigned
    /// to one (e.g. stockpiles with no district center yet).
    private static string? GetDistrictName(Inventory inventory)
    {
        var districtBuilding = inventory.GetComponent<DistrictBuilding>();
        return districtBuilding?.District?.DistrictName;
    }
}

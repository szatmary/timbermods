using System;
using System.Collections.Generic;
using System.Linq;
using Timberborn.GameDistricts;
using Timberborn.Goods;
using Timberborn.ResourceCountingSystem;

namespace LogBook.Metrics.Providers;

/// Registers one metric per good id. Uses the game's own
/// `ResourceCountingService` which aggregates stock across every source
/// (stockpiles, carried goods, output buffers, ground piles) — matches
/// what the game's top-bar resource widgets show.
public sealed class GoodsMetricProvider : IMetricProvider
{
    private readonly IGoodService _goodService;
    private readonly ResourceCountingService _resourceCounting;
    private readonly DistrictCenterRegistry _districts;

    public GoodsMetricProvider(
        IGoodService goodService,
        ResourceCountingService resourceCounting,
        DistrictCenterRegistry districts)
    {
        _goodService = goodService;
        _resourceCounting = resourceCounting;
        _districts = districts;
    }

    public IEnumerable<MetricDefinition> GetMetrics()
    {
        var goodIds = _goodService.Goods
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var goodId in goodIds)
        {
            var captured = goodId;
            yield return new MetricDefinition(
                id: $"good.{captured}",
                nameLocKey: $"Good.{captured}",
                category: MetricCategory.Goods,
                scope: MetricScope.Either,
                valueFn: districtName => TotalStock(captured, districtName));
        }
    }

    private float TotalStock(string goodId, string? districtName)
    {
        if (districtName is null)
            return _resourceCounting.GetGlobalResourceCount(goodId).AllStock;

        int sum = 0;
        foreach (var d in _districts.FinishedDistrictCenters)
        {
            if (d.DistrictName != districtName) continue;
            sum += _resourceCounting.GetDistrictResourceCounter(d).GetResourceCount(goodId).AllStock;
        }
        return sum;
    }
}

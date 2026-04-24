using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Timberborn.GameDistricts;
using Timberborn.Goods;
using Timberborn.ResourceCountingSystem;
using UnityEngine;

namespace Graphs.Metrics.Providers;

/// Registers one metric per good id. Uses the game's own
/// `ResourceCountingService` which aggregates stock across every source
/// (stockpiles, carried goods, output buffers, ground piles) — the same
/// total the game's top-bar resource widgets show.
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

    private static bool _loggedFailure;
    private static PropertyInfo? _allStockProp;

    private float TotalStock(string goodId, string? districtName)
    {
        try
        {
            if (districtName is null)
            {
                return ExtractAllStock(_resourceCounting.GetGlobalResourceCount(goodId));
            }

            int sum = 0;
            foreach (var d in _districts.FinishedDistrictCenters)
            {
                if (d.DistrictName != districtName) continue;
                var counter = _resourceCounting.GetDistrictResourceCounter(d);
                sum += ExtractAllStock(counter.GetResourceCount(goodId));
            }
            return sum;
        }
        catch (Exception ex)
        {
            if (!_loggedFailure)
            {
                _loggedFailure = true;
                Debug.LogWarning($"[Graphs] ResourceCounting lookup failed for '{goodId}': {ex.Message}");
            }
            return float.NaN;
        }
    }

    /// `ResourceCount` is a struct with `AllStock` etc. Resolve the property
    /// once via reflection so this compiles without binding to an exact shape
    /// (property name could shift across game versions).
    private static int ExtractAllStock(ResourceCount rc)
    {
        if (_allStockProp == null)
        {
            var type = typeof(ResourceCount);
            _allStockProp = type.GetProperty("AllStock")
                        ?? type.GetProperty("Stock")
                        ?? type.GetProperty("Amount");
        }
        if (_allStockProp == null) return 0;
        object? boxed = rc;
        var value = _allStockProp.GetValue(boxed);
        return value switch
        {
            int i => i,
            long l => (int)l,
            float f => (int)f,
            _ => 0,
        };
    }
}

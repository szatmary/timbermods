using System;
using System.Collections.Generic;
using System.Reflection;
using Timberborn.GameDistricts;
using Timberborn.GameFactionSystem;
using Timberborn.NeedSystem;
using Timberborn.Wellbeing;

namespace Graphs.Metrics.Providers;

/// Registers wellbeing metrics (average, minimum) and need-satisfaction metrics
/// (hunger/food, thirst/water) aggregated across beavers in the filter scope.
///
/// Key API notes — these differed from the NOTES.md hints and were confirmed
/// via metadata probe:
/// - `WellbeingService.GetAverageWellbeing(...)` does NOT exist. The aggregation
///   type `WellbeingTrackerRegistry` is internal, and both the global / district
///   wrappers (`GlobalWellbeingTrackerRegistry`, `DistrictWellbeingTrackerRegistry`)
///   are internal too — we can't inject them via Bindito. Instead we iterate
///   `district.GetComponent<DistrictPopulation>().Beavers` directly and read
///   `WellbeingTracker.Wellbeing` (which IS public) to compute min and average
///   ourselves. One pass serves both metrics.
/// - `WellbeingTracker.Wellbeing` is `int`, not `float`.
/// - `NeedManager` has no `GetSatisfaction`. Satisfaction is computed as
///   `GetNeedPoints(id) / GetNeedSpec(id).MaximumValue`.
/// - The "hunger" / "thirst" need ids are faction-specific (Folktails and Iron
///   Teeth use different spec ids). The resolved ids live as private static
///   strings on `NeedModificationService` (`FoodNeedId`, `WaterNeedId`).
///   We reflect them once on first use.
public sealed class WellbeingMetricProvider : IMetricProvider
{
    private readonly DistrictCenterRegistry _districtCenterRegistry;

    /// Lazily-resolved ids for the active faction's food and water needs.
    /// Null when reflection couldn't find the field (treat as "skip that metric").
    private string? _foodNeedId;
    private string? _waterNeedId;
    private bool _needIdsResolved;

    public WellbeingMetricProvider(DistrictCenterRegistry districtCenterRegistry)
    {
        _districtCenterRegistry = districtCenterRegistry;
    }

    public IEnumerable<MetricDefinition> GetMetrics()
    {
        yield return new MetricDefinition(
            id: "wellbeing.avg",
            nameLocKey: "Graphs.Metric.Wellbeing",
            category: MetricCategory.Wellbeing,
            scope: MetricScope.District,
            valueFn: districtName => WalkWellbeing(districtName).Average);

        // Hunger / thirst are inverted at the provider: we plot "unmet need"
        // rather than "satisfaction", so a rising line means beavers are
        // getting hungrier/thirstier — matches how bad things usually read
        // on charts (worse = higher).
        yield return new MetricDefinition(
            id: "need.hunger.avg",
            nameLocKey: "Graphs.Metric.Hunger",
            category: MetricCategory.Wellbeing,
            scope: MetricScope.District,
            valueFn: districtName => Invert(AverageSatisfaction(districtName, ResolveFoodNeedId())));

        yield return new MetricDefinition(
            id: "need.thirst.avg",
            nameLocKey: "Graphs.Metric.Thirst",
            category: MetricCategory.Wellbeing,
            scope: MetricScope.District,
            valueFn: districtName => Invert(AverageSatisfaction(districtName, ResolveWaterNeedId())));
    }

    private static float Invert(float satisfaction)
    {
        // NaN stays NaN; otherwise clamp to [0,1] and flip.
        if (float.IsNaN(satisfaction)) return float.NaN;
        if (satisfaction < 0) satisfaction = 0;
        if (satisfaction > 1) satisfaction = 1;
        return 1f - satisfaction;
    }

    private readonly struct WellbeingWalk
    {
        public readonly float Average;
        public readonly float Min;

        public WellbeingWalk(float average, float min)
        {
            Average = average;
            Min = min;
        }
    }

    /// Single pass over every beaver in the filter scope that collects both
    /// the average and the minimum of `WellbeingTracker.Wellbeing`. Returns
    /// NaN for both when no beaver is present.
    private WellbeingWalk WalkWellbeing(string? districtName)
    {
        long sum = 0;
        var count = 0;
        var min = int.MaxValue;
        foreach (var district in _districtCenterRegistry.FinishedDistrictCenters)
        {
            if (districtName != null && district.DistrictName != districtName)
            {
                continue;
            }
            var population = district.DistrictPopulation;
            if (population == null)
            {
                continue;
            }
            foreach (var beaver in population.Beavers)
            {
                WellbeingTracker? tracker;
                try
                {
                    tracker = beaver.GetComponent<WellbeingTracker>();
                }
                catch
                {
                    continue;
                }
                if (tracker == null)
                {
                    continue;
                }
                var w = tracker.Wellbeing;
                sum += w;
                count++;
                if (w < min)
                {
                    min = w;
                }
            }
        }
        if (count == 0)
        {
            return new WellbeingWalk(float.NaN, float.NaN);
        }
        return new WellbeingWalk((float)sum / count, min);
    }

    /// Average satisfaction (0..1) of a specific need across beavers in scope.
    /// Returns NaN when the need id couldn't be resolved (bad faction / probe
    /// failed) or when no beaver has the need active.
    private float AverageSatisfaction(string? districtName, string? needId)
    {
        if (needId == null)
        {
            return float.NaN;
        }
        float sum = 0f;
        var count = 0;
        foreach (var district in _districtCenterRegistry.FinishedDistrictCenters)
        {
            if (districtName != null && district.DistrictName != districtName)
            {
                continue;
            }
            var population = district.DistrictPopulation;
            if (population == null)
            {
                continue;
            }
            foreach (var beaver in population.Beavers)
            {
                try
                {
                    var needManager = beaver.GetComponent<NeedManager>();
                    if (needManager == null || !needManager.HasNeed(needId))
                    {
                        continue;
                    }
                    var spec = needManager.GetNeedSpec(needId);
                    if (spec == null) continue;
                    float min = spec.MinimumValue;
                    float max = spec.MaximumValue;
                    float range = max - min;
                    if (range <= 0f) continue;
                    var points = needManager.GetNeedPoints(needId);
                    // Normalize to 0..1 over the need's actual min..max range.
                    // 0 = at minimum (most unfavorable), 1 = at maximum (fully satisfied).
                    sum += (points - min) / range;
                    count++;
                }
                catch
                {
                    // Skip any beaver whose component lookup throws —
                    // keeps one broken entity from zeroing the metric.
                }
            }
        }
        return count > 0 ? sum / count : float.NaN;
    }

    private string? ResolveFoodNeedId()
    {
        EnsureNeedIdsResolved();
        return _foodNeedId;
    }

    private string? ResolveWaterNeedId()
    {
        EnsureNeedIdsResolved();
        return _waterNeedId;
    }

    /// `NeedModificationService` holds the active faction's food/water need ids
    /// in `static readonly string` fields populated at config time. Reflect them
    /// once on first access.
    private void EnsureNeedIdsResolved()
    {
        if (_needIdsResolved)
        {
            return;
        }
        _needIdsResolved = true;
        var type = typeof(NeedModificationService);
        _foodNeedId = ReadStaticString(type, "FoodNeedId");
        _waterNeedId = ReadStaticString(type, "WaterNeedId");
    }

    private static string? ReadStaticString(Type type, string fieldName)
    {
        var field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static);
        return field?.GetValue(null) as string;
    }
}

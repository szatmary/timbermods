using System;
using System.Collections.Generic;
using System.Reflection;
using Timberborn.GameDistricts;
using Timberborn.GameFactionSystem;
using Timberborn.NeedSystem;
using Timberborn.Wellbeing;

namespace LogBook.Metrics.Providers;

/// Registers wellbeing metrics (average, minimum) and need-satisfaction metrics
/// (hunger/food, thirst/water) aggregated across beavers in the filter scope.
/// The hunger/thirst need-ids are faction-specific and resolved via
/// reflection on `NeedModificationService` once on first use.
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
            nameLocKey: "LogBook.Metric.Wellbeing",
            category: MetricCategory.Wellbeing,
            scope: MetricScope.District,
            valueFn: districtName => WalkWellbeing(districtName).Average,
            // Wellbeing is on the 0..25ish scale (WellbeingTracker.Wellbeing
            // is an int), so it can't share a y-axis with the 0..1 hunger /
            // thirst lines — otherwise the need metrics collapse to zero.
            scaleGroup: "WellbeingAvg");

        // Hunger / thirst are inverted at the provider: we plot "unmet need"
        // rather than "satisfaction", so a rising line means beavers are
        // getting hungrier/thirstier — matches how bad things usually read
        // on charts (worse = higher).
        // Hunger / thirst are 0..1 (inverted need satisfaction). Pin the
        // y-axis to 1.0 so 0.03 reads near the bottom instead of stretching
        // to fill the chart.
        yield return new MetricDefinition(
            id: "need.hunger.avg",
            nameLocKey: "LogBook.Metric.Hunger",
            category: MetricCategory.Wellbeing,
            scope: MetricScope.District,
            valueFn: districtName => Invert(AverageSatisfaction(districtName, ResolveFoodNeedId())),
            fixedMax: 1f);

        yield return new MetricDefinition(
            id: "need.thirst.avg",
            nameLocKey: "LogBook.Metric.Thirst",
            category: MetricCategory.Wellbeing,
            scope: MetricScope.District,
            valueFn: districtName => Invert(AverageSatisfaction(districtName, ResolveWaterNeedId())),
            fixedMax: 1f);
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
                var tracker = beaver.GetComponent<WellbeingTracker>();
                if (tracker == null) continue;
                var w = tracker.Wellbeing;
                sum += w;
                count++;
                if (w < min) min = w;
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
                var needManager = beaver.GetComponent<NeedManager>();
                if (needManager == null || !needManager.HasNeed(needId)) continue;
                var spec = needManager.GetNeedSpec(needId);
                if (spec == null) continue;
                float min = spec.MinimumValue;
                float max = spec.MaximumValue;
                float range = max - min;
                if (range <= 0f) continue;
                // Normalize to 0..1 over the need's actual min..max range
                // (0 = minimum / most unfavourable, 1 = maximum / satisfied).
                sum += (needManager.GetNeedPoints(needId) - min) / range;
                count++;
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

using System.Collections.Generic;
using Timberborn.BeaverContaminationSystem;
using Timberborn.GameDistricts;
using Timberborn.Population;

namespace Graphs.Metrics.Providers;

/// Registers population-count metrics: head counts, housing/employment
/// shortfalls, and health-adjacent counts (infected/contaminated).
/// Everything funnels through the game's PopulationDataCollector where possible
/// so we don't re-implement aggregation logic.
///
/// TODO: injury detection — see NOTES.md. No `Injurable.IsInjured` boolean
/// exists; injury is encoded as an active `Need` with id `InjuryNeedId`. Adding
/// `pop.injured` means walking each Character's `Needs.AllNeeds` every sample,
/// which is heavier than the other metrics and needs a design pass.
public sealed class PopulationMetricProvider : IMetricProvider
{
    private readonly DistrictCenterRegistry _districtCenterRegistry;
    private readonly PopulationDataCollector _populationDataCollector;

    /// Scratch buffer reused for per-sample CollectData calls. PopulationData is a
    /// mutable class; the collector fills it in-place, so a singleton instance is fine.
    private readonly PopulationData _scratch = new();

    public PopulationMetricProvider(
        DistrictCenterRegistry districtCenterRegistry,
        PopulationDataCollector populationDataCollector)
    {
        _districtCenterRegistry = districtCenterRegistry;
        _populationDataCollector = populationDataCollector;
    }

    public IEnumerable<MetricDefinition> GetMetrics()
    {
        yield return FromDistrictData("pop.total", "Graphs.Metric.Total", d => d.TotalPopulation);
        yield return FromDistrictData("pop.adults", "Graphs.Metric.Adults", d => d.NumberOfAdults);
        yield return FromDistrictData("pop.kits", "Graphs.Metric.Kits", d => d.NumberOfChildren);
        yield return FromDistrictData("pop.bots", "Graphs.Metric.Bots", d => d.NumberOfBots);
        yield return FromDistrictData("pop.homeless", "Graphs.Metric.Homeless", d => d.BedData.Homeless);
        yield return FromDistrictData("pop.unemployed", "Graphs.Metric.Unemployed", d => d.BeaverWorkplaceData.Unemployed);
        yield return FromDistrictData("pop.contaminated", "Graphs.Metric.Contaminated", d => d.ContaminationData.ContaminatedTotal);

        yield return new MetricDefinition(
            id: "pop.infected",
            nameLocKey: "Graphs.Metric.Infected",
            category: MetricCategory.Population,
            scope: MetricScope.District,
            valueFn: CountInfected);
    }

    /// Build a metric whose value comes from a PopulationData field. The collector
    /// fills our scratch buffer once per district; we sum across all districts (or
    /// pick one when the filter selects a single district).
    private MetricDefinition FromDistrictData(string id, string locKey, System.Func<PopulationData, int> extract)
    {
        return new MetricDefinition(
            id: id,
            nameLocKey: locKey,
            category: MetricCategory.Population,
            scope: MetricScope.District,
            valueFn: districtName => SumAcross(districtName, extract));
    }

    /// Aggregate `extract` across districts matching the filter.
    /// When `districtName` is null, sums every finished district; otherwise returns
    /// the single matching district's value, or 0 if no district has that name.
    private float SumAcross(string? districtName, System.Func<PopulationData, int> extract)
    {
        var total = 0;
        foreach (var district in _districtCenterRegistry.FinishedDistrictCenters)
        {
            if (districtName != null && district.DistrictName != districtName)
            {
                continue;
            }
            if (!_populationDataCollector.CollectData(district, _scratch))
            {
                continue;
            }
            total += extract(_scratch);
        }
        return total;
    }

    /// Count beavers whose ContaminationIncubator is currently running (pre-
    /// contamination illness). PopulationData doesn't track incubation separately,
    /// so we walk each district's Beavers list and probe for the component.
    /// Bots can't be infected, so beavers-only is the right set to scan.
    private float CountInfected(string? districtName)
    {
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
                var incubator = beaver.GetComponent<ContaminationIncubator>();
                if (incubator != null && incubator.IsIncubating)
                {
                    count++;
                }
            }
        }
        return count;
    }
}

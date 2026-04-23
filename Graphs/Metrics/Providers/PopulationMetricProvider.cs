using System.Collections.Generic;
using Timberborn.GameDistricts;
using Timberborn.Population;

namespace Graphs.Metrics.Providers;

/// Registers population-count metrics: total/adults/kits/bots counts plus
/// derived counts (homeless/unemployed) that the game already aggregates
/// per-district and globally via PopulationDataCollector.
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
        yield return Define("pop.total", "Graphs.Metric.Total", d => d.TotalPopulation);
        yield return Define("pop.adults", "Graphs.Metric.Adults", d => d.NumberOfAdults);
        yield return Define("pop.kits", "Graphs.Metric.Kits", d => d.NumberOfChildren);
        yield return Define("pop.bots", "Graphs.Metric.Bots", d => d.NumberOfBots);
        yield return Define("pop.homeless", "Graphs.Metric.Homeless", d => d.BedData.Homeless);
        yield return Define("pop.unemployed", "Graphs.Metric.Unemployed", d => d.BeaverWorkplaceData.Unemployed);
    }

    private MetricDefinition Define(string id, string locKey, System.Func<PopulationData, int> extract)
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
}

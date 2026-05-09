using System.Collections.Generic;
using Timberborn.BeaverContaminationSystem;
using Timberborn.GameDistricts;
using Timberborn.Population;

namespace LogBook.Metrics.Providers;

/// Population head counts, housing/employment shortfalls, and contamination
/// counters. Settlement-wide values come from `PopulationService.GlobalPopulationData`
/// which the game updates continuously. Per-district values use
/// `PopulationDataCollector.CollectData` against the selected district.
public sealed class PopulationMetricProvider : IMetricProvider
{
    private readonly PopulationService _populationService;
    private readonly PopulationDataCollector _populationDataCollector;
    private readonly DistrictCenterRegistry _districtCenterRegistry;

    private readonly PopulationData _scratch = new();

    public PopulationMetricProvider(
        PopulationService populationService,
        PopulationDataCollector populationDataCollector,
        DistrictCenterRegistry districtCenterRegistry)
    {
        _populationService = populationService;
        _populationDataCollector = populationDataCollector;
        _districtCenterRegistry = districtCenterRegistry;
    }

    public IEnumerable<MetricDefinition> GetMetrics()
    {
        // Totals
        yield return FromData("pop.total",  "LogBook.Metric.Total",  d => d.TotalPopulation, "Population");
        yield return FromData("pop.adults", "LogBook.Metric.Adults", d => d.NumberOfAdults,   "Population");
        yield return FromData("pop.kits",   "LogBook.Metric.Kits",   d => d.NumberOfChildren, "Population");
        yield return FromData("pop.bots",   "LogBook.Metric.Bots",   d => d.NumberOfBots,     "Population");

        // Quarters (beds)
        yield return FromData("pop.homeless",      "LogBook.Metric.Homeless",     d => d.BedData.Homeless,     "Quarters");
        yield return FromData("pop.beds.occupied", "LogBook.Metric.OccupiedBeds", d => d.BedData.OccupiedBeds, "Quarters");
        yield return FromData("pop.beds.free",     "LogBook.Metric.FreeBeds",     d => d.BedData.FreeBeds,     "Quarters");

        // Employment — its own top-level category; sub-grouped by workforce
        // (all beaver metrics together, then all bot metrics).
        yield return InCategory(MetricCategory.Employment,
            "emp.jobs.beavers", "LogBook.Metric.BeaverJobs",
            d => d.BeaverWorkplaceData.OccupiedWorkslots, "Beavers");
        yield return InCategory(MetricCategory.Employment,
            "emp.unemployed.beavers", "LogBook.Metric.UnemployedBeavers",
            d => d.BeaverWorkplaceData.Unemployed, "Beavers");
        yield return InCategory(MetricCategory.Employment,
            "emp.vacancies.beavers", "LogBook.Metric.BeaverVacancies",
            d => d.BeaverWorkplaceData.FreeWorkslots, "Beavers");
        yield return InCategory(MetricCategory.Employment,
            "emp.jobs.bots", "LogBook.Metric.BotJobs",
            d => d.BotWorkplaceData.OccupiedWorkslots, "Bots");
        yield return InCategory(MetricCategory.Employment,
            "emp.unemployed.bots", "LogBook.Metric.UnemployedBots",
            d => d.BotWorkplaceData.Unemployed, "Bots");
        yield return InCategory(MetricCategory.Employment,
            "emp.vacancies.bots", "LogBook.Metric.BotVacancies",
            d => d.BotWorkplaceData.FreeWorkslots, "Bots");

        // Health
        yield return FromData("pop.contaminated", "LogBook.Metric.Contaminated",
            d => d.ContaminationData.ContaminatedTotal, "Health");

        yield return new MetricDefinition(
            id: "pop.infected",
            nameLocKey: "LogBook.Metric.Infected",
            category: MetricCategory.Population,
            scope: MetricScope.District,
            valueFn: CountInfected);
    }

    private MetricDefinition FromData(
        string id, string locKey, System.Func<PopulationData, int> extract, string? subGroup = null)
        => InCategory(MetricCategory.Population, id, locKey, extract, subGroup);

    private MetricDefinition InCategory(
        MetricCategory category, string id, string locKey,
        System.Func<PopulationData, int> extract,
        string? subGroup = null, string? scaleGroup = null) =>
        new(id, locKey, category, MetricScope.District,
            districtName => ExtractFor(districtName, extract), subGroup, scaleGroup);

    private float ExtractFor(string? districtName, System.Func<PopulationData, int> extract)
    {
        if (districtName is null)
        {
            // Settlement-wide: use the game's already-aggregated data.
            var data = _populationService.GlobalPopulationData;
            return data == null ? 0 : extract(data);
        }

        // Per-district: collect into scratch.
        foreach (var district in _districtCenterRegistry.FinishedDistrictCenters)
        {
            if (district.DistrictName != districtName) continue;
            _populationDataCollector.CollectData(district, _scratch);
            return extract(_scratch);
        }
        return 0;
    }

    private float CountInfected(string? districtName)
    {
        int count = 0;
        foreach (var district in _districtCenterRegistry.FinishedDistrictCenters)
        {
            if (districtName != null && district.DistrictName != districtName) continue;
            var population = district.DistrictPopulation;
            if (population == null) continue;
            foreach (var beaver in population.Beavers)
            {
                var incubator = beaver.GetComponent<ContaminationIncubator>();
                if (incubator != null && incubator.IsIncubating) count++;
            }
        }
        return count;
    }
}

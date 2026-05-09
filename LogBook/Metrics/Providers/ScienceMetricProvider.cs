using System.Collections.Generic;
using Timberborn.ScienceSystem;

namespace LogBook.Metrics.Providers;

public sealed class ScienceMetricProvider : IMetricProvider
{
    private readonly ScienceService _scienceService;

    public ScienceMetricProvider(ScienceService scienceService)
    {
        _scienceService = scienceService;
    }

    public IEnumerable<MetricDefinition> GetMetrics()
    {
        yield return new MetricDefinition(
            id: "science.stored",
            nameLocKey: "LogBook.Metric.ScienceStored",
            category: MetricCategory.Science,
            scope: MetricScope.Settlement,
            valueFn: _ => _scienceService.SciencePoints);
    }
}

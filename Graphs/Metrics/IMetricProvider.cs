using System.Collections.Generic;

namespace Graphs.Metrics;

/// Implemented by each category-specific provider. Providers are collected
/// by the registry at Load(). Providers whose backing game services aren't
/// available (e.g. Iron Teeth-only in a Folktails save) can return an empty
/// list and the registry will ignore them.
public interface IMetricProvider
{
    IEnumerable<MetricDefinition> GetMetrics();
}

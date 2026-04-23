namespace Graphs.Metrics;

/// A single trackable metric. The value function is called once per sample
/// (per in-game hour) with the active district-filter value.
public sealed class MetricDefinition
{
    public string Id { get; }
    public string NameLocKey { get; }
    public MetricCategory Category { get; }
    public MetricScope Scope { get; }

    /// <param name="districtName">
    /// Specific district name when the filter is set to one district, or
    /// null for "all districts" (settlement-wide aggregation).
    /// </param>
    public Func<string?, float> ValueFn { get; }

    public MetricDefinition(
        string id,
        string nameLocKey,
        MetricCategory category,
        MetricScope scope,
        Func<string?, float> valueFn)
    {
        Id = id;
        NameLocKey = nameLocKey;
        Category = category;
        Scope = scope;
        ValueFn = valueFn;
    }
}

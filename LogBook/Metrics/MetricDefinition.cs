namespace LogBook.Metrics;

/// A single trackable metric. The value function is called once per sample
/// (per in-game hour) with the active district-filter value.
public sealed class MetricDefinition
{
    public string Id { get; }
    public string NameLocKey { get; }
    public MetricCategory Category { get; }
    public MetricScope Scope { get; }

    /// Optional sub-group within the category, used by the legend to cluster
    /// related metrics (e.g. "Quarters" for homeless/beds inside Population).
    /// Null = no sub-grouping.
    public string? SubGroup { get; }

    /// Optional chart-scale override. The chart shares a y-scale across all
    /// metrics with the same `ScaleGroup` key. When null, the Category name
    /// is used — so by default each category has its own scale. Setting this
    /// lets categories that are visually separate (e.g. Bots vs Population)
    /// share a scale for direct comparison.
    public string ScaleGroup { get; }

    /// Optional soft upper bound for this metric's y-axis. The chart uses
    /// `max(observed, FixedMax)` as the ceiling — so a metric with a known
    /// natural top (e.g. 0..1 satisfaction) reads correctly when values
    /// are tiny, but the axis still grows if the data ever exceeds the
    /// declared bound. If multiple metrics in the same ScaleGroup declare
    /// a FixedMax, the group uses the largest of them.
    public float? FixedMax { get; }

    /// Optional soft lower bound for this metric's y-axis. The chart uses
    /// `min(observed, FixedMin)` as the floor — lets a metric that can go
    /// negative (e.g. wellbeing) maintain a stable visual baseline instead
    /// of the axis jittering as values cross zero, while still expanding
    /// downward if the data goes further negative. If multiple metrics in
    /// the same ScaleGroup declare a FixedMin, the group uses the smallest.
    public float? FixedMin { get; }

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
        Func<string?, float> valueFn,
        string? subGroup = null,
        string? scaleGroup = null,
        float? fixedMax = null,
        float? fixedMin = null)
    {
        Id = id;
        NameLocKey = nameLocKey;
        Category = category;
        Scope = scope;
        ValueFn = valueFn;
        SubGroup = subGroup;
        ScaleGroup = scaleGroup ?? category.ToString();
        FixedMax = fixedMax;
        FixedMin = fixedMin;
    }
}

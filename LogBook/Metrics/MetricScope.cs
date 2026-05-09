namespace LogBook.Metrics;

/// Scope a metric's value function operates at.
public enum MetricScope
{
    /// Value is settlement-wide; the district filter is ignored.
    Settlement,

    /// Value is per-district; "All districts" aggregates, a specific district filters.
    District,

    /// Value works either way; provider handles both.
    Either,
}

using System;

namespace Graphs.Metrics;

/// Shared mutable state: which district the user has selected in the legend.
/// `null` means "all districts" (settlement-wide aggregation).
/// The UI mutates this; the sampler reads it at sample time.
public sealed class DistrictFilter
{
    public string? DistrictName { get; private set; }
    public event Action? Changed;

    public void Set(string? districtName)
    {
        if (DistrictName == districtName) return;
        DistrictName = districtName;
        Changed?.Invoke();
    }
}

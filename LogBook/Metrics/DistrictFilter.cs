using System;
using System.Linq;
using Timberborn.EntitySystem;
using Timberborn.GameDistricts;
using Timberborn.SingletonSystem;

namespace LogBook.Metrics;

/// Shared mutable state: the district selected in the legend. `null` means
/// "all districts" (settlement-wide aggregation). Holds a `DistrictCenter`
/// reference rather than a name so renames don't break the filter.
public sealed class DistrictFilter : ILoadableSingleton
{
    private readonly DistrictCenterRegistry _registry;
    private readonly EventBus _eventBus;
    private DistrictCenter? _districtCenter;

    public string? DistrictName => _districtCenter?.DistrictName;

    /// Stable per-district identifier — survives renames. Null = "all
    /// districts" (settlement-wide). Used as the key into the sampler's
    /// per-district history map.
    public Guid? DistrictId => GetEntityId(_districtCenter);

    public static Guid? GetEntityId(DistrictCenter? dc)
    {
        if (dc == null) return null;
        return dc.TryGetComponent(out EntityComponent ec) ? ec.EntityId : (Guid?)null;
    }

    public event Action? Changed;

    public DistrictFilter(DistrictCenterRegistry registry, EventBus eventBus)
    {
        _registry = registry;
        _eventBus = eventBus;
    }

    public void Load()
    {
        _eventBus.Register(this);
    }

    public void Set(string? districtName)
    {
        DistrictCenter? next = districtName == null
            ? null
            : _registry.FinishedDistrictCenters
                .FirstOrDefault(d => d.DistrictName == districtName);
        if (ReferenceEquals(next, _districtCenter)) return;
        _districtCenter = next;
        Changed?.Invoke();
    }

    /// If the held district has been removed, fall back to "all districts".
    [OnEvent]
    public void OnDistrictCenterRegistryChanged(DistrictCenterRegistryChangedEvent _)
    {
        if (_districtCenter == null) return;
        if (!_registry.FinishedDistrictCenters.Contains(_districtCenter))
        {
            _districtCenter = null;
            Changed?.Invoke();
        }
    }
}

using System;
using System.Linq;
using Timberborn.EntitySystem;
using Timberborn.GameDistricts;
using Timberborn.SingletonSystem;
using UnityEngine;

namespace Graphs.Metrics;

/// Shared mutable state: which district the user has selected in the legend.
/// `null` means "all districts" (settlement-wide aggregation).
///
/// The filter holds a `DistrictCenter` reference rather than a name string so
/// renaming a district doesn't break the filter — providers always see the
/// district's *current* name. Deletion of the held district drops the filter
/// back to "all districts".
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

    public DistrictCenter? DistrictCenter => _districtCenter;

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

    /// Re-validate when the registry mutates — if our held district was
    /// removed, fall back to "all districts" so the filter stops asking
    /// providers about a phantom district.
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

using System;
using System.Collections.Generic;
using System.Linq;
using Graphs.Metrics;
using Timberborn.DropdownSystem;
using Timberborn.GameDistricts;
using Timberborn.SingletonSystem;
using UnityEngine.UIElements;

namespace Graphs.UI;

/// Builds the district dropdown using the game's native `Dropdown` widget.
/// `DropdownInitializer` (registered as an IVisualElementInitializer) wires
/// the dropdown up when VisualElementInitializer walks our tree; we then call
/// `DropdownItemsSetter.SetItems` to populate it from a live provider so
/// renames / adds / removes show up.
public sealed class GraphsDistrictSelector : ILoadableSingleton
{
    public const string AllDistrictsLabel = "All districts";

    private readonly DistrictCenterRegistry _registry;
    private readonly DistrictFilter _filter;
    private readonly DropdownItemsSetter _itemsSetter;
    private readonly EventBus _eventBus;

    private Dropdown? _dropdown;
    private DistrictDropdownProvider? _provider;

    public GraphsDistrictSelector(
        DistrictCenterRegistry registry,
        DistrictFilter filter,
        DropdownItemsSetter itemsSetter,
        EventBus eventBus)
    {
        _registry = registry;
        _filter = filter;
        _itemsSetter = itemsSetter;
        _eventBus = eventBus;
    }

    public void Load()
    {
        _eventBus.Register(this);
    }

    public VisualElement Build()
    {
        _provider = new DistrictDropdownProvider(_registry, _filter);
        _dropdown = new Dropdown();
        _dropdown.style.marginBottom = 8;

        // Items are set after VisualElementInitializer runs — at which point
        // DropdownInitializer has called Initialize(DropdownListDrawer) on
        // our instance and the inner buttons exist.
        _dropdown.RegisterCallback<AttachToPanelEvent>(_ =>
        {
            if (_dropdown != null && _provider != null)
                _itemsSetter.SetItems(_dropdown, _provider);
        });

        return _dropdown;
    }

    /// Refresh dropdown items when districts come or go. (Renames don't fire
    /// this event but the provider's Items list is computed live, so the
    /// next time the dropdown is opened it'll show current names.)
    [OnEvent]
    public void OnDistrictCenterRegistryChanged(DistrictCenterRegistryChangedEvent _)
    {
        if (_dropdown != null && _provider != null)
            _itemsSetter.SetItems(_dropdown, _provider);
    }
}

/// Live IDropdownProvider — `Items` is a computed property so renames / adds
/// / removes are picked up at every query. Maps the "All districts" UI
/// sentinel to the DistrictFilter's null value.
internal sealed class DistrictDropdownProvider : IDropdownProvider
{
    private readonly DistrictCenterRegistry _registry;
    private readonly DistrictFilter _filter;

    public DistrictDropdownProvider(DistrictCenterRegistry registry, DistrictFilter filter)
    {
        _registry = registry;
        _filter = filter;
    }

    public IReadOnlyList<string> Items
    {
        get
        {
            var list = new List<string> { GraphsDistrictSelector.AllDistrictsLabel };
            list.AddRange(_registry.FinishedDistrictCenters
                .Select(d => d.DistrictName)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
            return list;
        }
    }

    public string GetValue() => _filter.DistrictName ?? GraphsDistrictSelector.AllDistrictsLabel;

    public void SetValue(string value)
    {
        _filter.Set(value == GraphsDistrictSelector.AllDistrictsLabel ? null : value);
    }
}

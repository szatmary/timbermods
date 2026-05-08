using System;
using System.Collections.Generic;
using System.Linq;
using Graphs.Metrics;
using Timberborn.DropdownSystem;
using Timberborn.GameDistricts;
using Timberborn.SingletonSystem;
using UnityEngine.UIElements;

namespace Graphs.UI;

/// District dropdown using the game's native `Dropdown` widget. The
/// game's DropdownInitializer wires it up when VisualElementInitializer
/// walks our tree; we then populate items via DropdownItemsSetter against
/// a live provider so renames / adds / removes show up.
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

        // Set items only after VisualElementInitializer has wired the
        // dropdown's inner buttons through DropdownInitializer.
        _dropdown.RegisterCallback<AttachToPanelEvent>(_ =>
        {
            if (_dropdown != null && _provider != null)
                _itemsSetter.SetItems(_dropdown, _provider);
        });

        return _dropdown;
    }

    /// Refresh dropdown items when districts are added or removed. Renames
    /// don't fire this event, but the provider's Items list is computed
    /// live so the next dropdown open will show current names.
    [OnEvent]
    public void OnDistrictCenterRegistryChanged(DistrictCenterRegistryChangedEvent _)
    {
        if (_dropdown != null && _provider != null)
            _itemsSetter.SetItems(_dropdown, _provider);
    }
}

/// Live IDropdownProvider — `Items` recomputes on every query, so renames
/// surface immediately. The "All districts" UI label maps to a null filter.
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

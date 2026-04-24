using System;
using System.Collections.Generic;
using System.Linq;
using Graphs.Metrics;
using Timberborn.DropdownSystem;
using Timberborn.CoreUI;
using Timberborn.Localization;
using Timberborn.TooltipSystem;
using Timberborn.AssetSystem;
using UnityEngine.UIElements;

namespace Graphs.UI;

/// Builds the district dropdown using the game's native `Dropdown` widget.
/// `DropdownInitializer` (registered as an IVisualElementInitializer) wires
/// the dropdown up when VisualElementInitializer walks our tree; we then call
/// `DropdownItemsSetter.SetItems` to populate it from a string provider.
public sealed class GraphsDistrictSelector
{
    private const string AllDistrictsLabel = "All districts";

    private readonly Timberborn.GameDistricts.DistrictCenterRegistry _districts;
    private readonly DistrictFilter _filter;
    private readonly DropdownItemsSetter _itemsSetter;

    private Dropdown? _dropdown;
    private DistrictDropdownProvider? _provider;

    public GraphsDistrictSelector(
        Timberborn.GameDistricts.DistrictCenterRegistry districts,
        DistrictFilter filter,
        DropdownItemsSetter itemsSetter)
    {
        _districts = districts;
        _filter = filter;
        _itemsSetter = itemsSetter;
    }

    public VisualElement Build()
    {
        var names = new List<string> { AllDistrictsLabel };
        names.AddRange(_districts.FinishedDistrictCenters
            .Select(d => d.DistrictName)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase));

        _provider = new DistrictDropdownProvider(names, _filter);
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
}

/// Minimal IDropdownProvider for the district filter. Maps the "All districts"
/// UI sentinel to the DistrictFilter's null value.
internal sealed class DistrictDropdownProvider : IDropdownProvider
{
    private const string AllSentinel = "All districts";
    private readonly DistrictFilter _filter;

    public IReadOnlyList<string> Items { get; }

    public DistrictDropdownProvider(IReadOnlyList<string> items, DistrictFilter filter)
    {
        Items = items;
        _filter = filter;
    }

    public string GetValue() => _filter.DistrictName ?? AllSentinel;

    public void SetValue(string value)
    {
        _filter.Set(value == AllSentinel ? null : value);
    }
}

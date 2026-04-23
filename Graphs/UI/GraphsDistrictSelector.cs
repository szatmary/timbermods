using System.Collections.Generic;
using System.Linq;
using Graphs.Metrics;
using Timberborn.GameDistricts;
using UnityEngine.UIElements;

namespace Graphs.UI;

public sealed class GraphsDistrictSelector
{
    private const string AllDistrictsLabel = "All districts";

    private readonly DistrictCenterRegistry _districts;
    private readonly DistrictFilter _filter;

    public GraphsDistrictSelector(DistrictCenterRegistry districts, DistrictFilter filter)
    {
        _districts = districts;
        _filter = filter;
    }

    public VisualElement Build()
    {
        var names = new List<string> { AllDistrictsLabel };
        names.AddRange(_districts.FinishedDistrictCenters
            .Select(d => d.DistrictName)
            .OrderBy(n => n, System.StringComparer.OrdinalIgnoreCase));

        var dropdown = new DropdownField("District", names,
            _filter.DistrictName is null ? 0 : System.Math.Max(0, names.IndexOf(_filter.DistrictName)));
        dropdown.AddToClassList("game-dropdown");
        dropdown.style.marginBottom = 8;
        dropdown.RegisterValueChangedCallback(evt =>
        {
            _filter.Set(evt.newValue == AllDistrictsLabel ? null : evt.newValue);
        });
        return dropdown;
    }
}

using System.Collections.Generic;
using Timberborn.AssetSystem;
using UnityEngine;

namespace LogBook.UI;

/// Resolves the game's native UI sprites for our metric ids via
/// `IAssetLoader.Load<Sprite>`. Cached per id.
public sealed class GameIcons
{
    private readonly IAssetLoader _assets;
    private readonly Dictionary<string, Sprite?> _cache = new();

    private static readonly Dictionary<string, string> MetricToPath = new()
    {
        { "pop.total",             "UI/Images/Game/ico-beavers" },
        { "pop.adults",            "UI/Images/Game/ico-adult" },
        { "pop.kits",              "UI/Images/Game/ico-child" },
        { "pop.bots",              "UI/Images/Game/ico-bot" },
        { "pop.homeless",          "UI/Images/Game/ico-no-bed" },
        { "pop.beds.occupied",     "UI/Images/Game/ico-bed" },
        { "pop.beds.free",         "UI/Images/Game/ico-bed-empty" },
        { "emp.unemployed.beavers", "UI/Images/Game/ico-no-work-beaver" },
        { "emp.unemployed.bots",   "UI/Images/Game/ico-no-work-bot" },
        { "emp.jobs.beavers",      "UI/Images/Game/ico-work-beaver" },
        { "emp.jobs.bots",         "UI/Images/Game/ico-work-bot" },
        { "emp.vacancies.beavers", "UI/Images/Game/ico-work-empty-beaver" },
        { "emp.vacancies.bots",    "UI/Images/Game/ico-work-empty-bot" },
        { "pop.contaminated",      "UI/Images/Game/ico-contamination" },
        { "pop.infected",          "UI/Images/Game/ico-contamination" },
        { "science.stored",        "UI/Images/Game/science-icon" },
        // Weather entries aren't metrics — the chart looks them up via the
        // same path to label drought/badtide bands with native banner art.
        { "weather.drought",       "UI/Images/Game/weather-notification-dry" },
        { "weather.badtide",       "UI/Images/Game/weather-notification-badtide" },
    };

    public GameIcons(IAssetLoader assets)
    {
        _assets = assets;
    }

    public Sprite? TryGet(string metricId)
    {
        if (!MetricToPath.TryGetValue(metricId, out var path)) return null;
        if (_cache.TryGetValue(metricId, out var cached)) return cached;

        Sprite? sprite = null;
        foreach (var la in _assets.LoadAll<Sprite>(path))
        {
            if (la.Asset != null) { sprite = la.Asset; break; }
        }

        _cache[metricId] = sprite;
        return sprite;
    }
}

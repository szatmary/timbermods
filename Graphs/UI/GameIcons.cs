using System.Collections.Generic;
using Timberborn.AssetSystem;
using UnityEngine;

namespace Graphs.UI;

/// Resolves the game's native UI sprites for population/science metrics.
/// Uses `IAssetLoader.Load<Sprite>` against paths found in the asset bundles
/// (UI/Images/Game/ico-*). Caches results per metric id.
public sealed class GameIcons
{
    private readonly IAssetLoader _assets;
    private readonly Dictionary<string, Sprite?> _cache = new();

    // Paths come from `sharedassets*.assets` string probe. Timberborn loads
    // them via IAssetLoader at UI-build time.
    private static readonly Dictionary<string, string> MetricToPath = new()
    {
        // Population totals
        { "pop.total",             "UI/Images/Game/ico-beavers" },
        { "pop.adults",            "UI/Images/Game/ico-adult" },
        { "pop.kits",              "UI/Images/Game/ico-child" },
        { "pop.bots",              "UI/Images/Game/ico-bot" },
        // Quarters — use the game's bed icons (same ones used in the dwelling
        // tab in the upper-left HUD).
        { "pop.homeless",          "UI/Images/Game/ico-no-bed" },
        { "pop.beds.occupied",     "UI/Images/Game/ico-bed" },
        { "pop.beds.free",         "UI/Images/Game/ico-bed-empty" },
        // Employment
        { "emp.unemployed.beavers", "UI/Images/Game/ico-no-work-beaver" },
        { "emp.unemployed.bots",   "UI/Images/Game/ico-no-work-bot" },
        { "emp.jobs.beavers",      "UI/Images/Game/ico-work-beaver" },
        { "emp.jobs.bots",         "UI/Images/Game/ico-work-bot" },
        { "emp.vacancies.beavers", "UI/Images/Game/ico-work-empty-beaver" },
        { "emp.vacancies.bots",    "UI/Images/Game/ico-work-empty-bot" },
        // Health
        { "pop.contaminated",      "UI/Images/Game/ico-contamination" },
        { "pop.infected",          "UI/Images/Game/ico-contamination" },
        // Science
        { "science.stored",        "UI/Images/Game/science-icon" },
        // Weather — not metrics, just loaded via the same path so the chart
        // can label its drought/badtide bands with the game's native banner
        // sprites (the wide "weather changed" notifications that flash in
        // the upper-right of the HUD, not the small square icons).
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
        // LoadAll returns the LoadedAsset list for a path; a single asset
        // surfaces as the first non-null entry.
        foreach (var la in _assets.LoadAll<Sprite>(path))
        {
            if (la.Asset != null) { sprite = la.Asset; break; }
        }

        _cache[metricId] = sprite;
        return sprite;
    }
}

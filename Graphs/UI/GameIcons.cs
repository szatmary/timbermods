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
        try
        {
            // LoadAll is the typical Timberborn pattern — it returns a
            // LoadedAsset list. For a single asset we just take the first.
            var loaded = _assets.LoadAll<Sprite>(path);
            foreach (var la in loaded)
            {
                if (la.Asset != null) { sprite = la.Asset; break; }
            }
        }
        catch { /* asset missing; stay null */ }

        _cache[metricId] = sprite;
        return sprite;
    }
}

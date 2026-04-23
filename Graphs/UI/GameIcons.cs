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
        { "pop.total",             "UI/Images/Game/ico-beavers" },
        { "pop.adults",            "UI/Images/Game/ico-adult" },
        { "pop.kits",              "UI/Images/Game/ico-child" },
        { "pop.bots",              "UI/Images/Game/ico-bot" },
        { "pop.homeless",          "UI/Images/Game/homeless" },
        { "pop.unemployed.beavers", "UI/Images/Game/ico-no-work-beaver" },
        { "pop.unemployed.bots",   "UI/Images/Game/ico-no-work-bot" },
        { "pop.jobs.beavers",      "UI/Images/Game/ico-work-beaver" },
        { "pop.jobs.bots",         "UI/Images/Game/ico-work-bot" },
        { "pop.vacancies.beavers", "UI/Images/Game/ico-work-empty-beaver" },
        { "pop.vacancies.bots",    "UI/Images/Game/ico-work-empty-bot" },
        { "pop.contaminated",      "UI/Images/Game/ico-contamination" },
        { "pop.infected",          "UI/Images/Game/ico-contamination" },
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

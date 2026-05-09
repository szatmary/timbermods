using System.Collections.Generic;
using System.Linq;
using Timberborn.SingletonSystem;
using UnityEngine;

namespace LogBook.Metrics;

/// Flattens all registered IMetricProviders into a single ordered metric list
/// available to both the sampler and the UI. Order is:
///   1. By category enum order
///   2. Within category, by provider iteration order (providers append in
///      the order they want; goods provider sorts by display key).
public sealed class MetricRegistry : ILoadableSingleton
{
    private readonly IEnumerable<IMetricProvider> _providers;
    private readonly List<MetricDefinition> _metrics = new();
    private readonly Dictionary<string, int> _idToIndex = new();

    public IReadOnlyList<MetricDefinition> Metrics => _metrics;
    public int Count => _metrics.Count;

    public MetricRegistry(IEnumerable<IMetricProvider> providers)
    {
        _providers = providers;
    }

    public void Load()
    {
        var byCategory = new SortedDictionary<MetricCategory, List<MetricDefinition>>();

        foreach (var provider in _providers)
        {
            IEnumerable<MetricDefinition> defs;
            try
            {
                defs = provider.GetMetrics();
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[LogBook] Provider {provider.GetType().Name} failed: {ex.Message}");
                continue;
            }

            foreach (var def in defs)
            {
                if (!byCategory.TryGetValue(def.Category, out var list))
                {
                    list = new List<MetricDefinition>();
                    byCategory[def.Category] = list;
                }
                list.Add(def);
            }
        }

        foreach (var list in byCategory.Values)
            foreach (var def in list)
            {
                if (_idToIndex.ContainsKey(def.Id))
                {
                    Debug.LogWarning($"[LogBook] Duplicate metric id ignored: {def.Id}");
                    continue;
                }
                _idToIndex[def.Id] = _metrics.Count;
                _metrics.Add(def);
            }

        Debug.Log($"[LogBook] MetricRegistry loaded {_metrics.Count} metrics.");
    }

    public int IndexOf(string id) =>
        _idToIndex.TryGetValue(id, out var i) ? i : -1;
}

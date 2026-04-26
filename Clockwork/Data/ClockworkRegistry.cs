using System;
using System.Collections.Generic;
using System.Linq;
using Timberborn.Persistence;
using Timberborn.SingletonSystem;
using Timberborn.WorldPersistence;

namespace Clockwork.Data;

/// Persistent name-per-anchor map. The anchor is one automator within a
/// vanilla AutomatorPartition; the partition's display name in the drawer
/// is the name of any anchor it currently contains.
public sealed class ClockworkRegistry : ILoadableSingleton, ISaveableSingleton
{
    private static readonly SingletonKey SavedKey = new("ClockworkRegistry");
    private static readonly ListKey<string> SavedAnchorIds = new("AnchorIds");
    private static readonly ListKey<string> SavedNames = new("Names");

    private readonly ISingletonLoader _singletonLoader;
    private readonly Dictionary<Guid, string> _namesByAnchor = new();

    public event Action? Changed;

    public ClockworkRegistry(ISingletonLoader singletonLoader)
    {
        _singletonLoader = singletonLoader;
    }

    public void Load()
    {
        if (!_singletonLoader.TryGetSingleton(SavedKey, out var loader)) return;
        try
        {
            var ids = loader.Get(SavedAnchorIds);
            var names = loader.Get(SavedNames);
            int n = Math.Min(ids.Count, names.Count);
            for (int i = 0; i < n; i++)
            {
                if (Guid.TryParse(ids[i], out var g))
                    _namesByAnchor[g] = names[i];
            }
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning($"[Clockwork] registry restore failed: {ex.Message}");
        }
    }

    public void Save(ISingletonSaver singletonSaver)
    {
        var saver = singletonSaver.GetSingleton(SavedKey);
        saver.Set(SavedAnchorIds, _namesByAnchor.Keys.Select(g => g.ToString()).ToArray());
        saver.Set(SavedNames, _namesByAnchor.Values.ToArray());
    }

    public bool TryGet(Guid anchor, out string name)
        => _namesByAnchor.TryGetValue(anchor, out name!);

    public void Set(Guid anchor, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) { Remove(anchor); return; }
        _namesByAnchor[anchor] = name;
        Changed?.Invoke();
    }

    public void Remove(Guid anchor)
    {
        if (_namesByAnchor.Remove(anchor)) Changed?.Invoke();
    }

    public IReadOnlyDictionary<Guid, string> All => _namesByAnchor;
}

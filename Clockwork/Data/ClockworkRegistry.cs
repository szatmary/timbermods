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
///
/// Keys are `Automator.AutomatorId` strings (vanilla owns the id format).
public sealed class ClockworkRegistry : ILoadableSingleton, ISaveableSingleton
{
    private static readonly SingletonKey SavedKey = new("ClockworkRegistry");
    private static readonly ListKey<string> SavedAnchorIds = new("AnchorIds");
    private static readonly ListKey<string> SavedNames = new("Names");

    private readonly ISingletonLoader _singletonLoader;
    private readonly Dictionary<string, string> _namesByAnchor = new(StringComparer.Ordinal);

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
                _namesByAnchor[ids[i]] = names[i];
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning($"[Clockwork] registry restore failed: {ex.Message}");
        }
    }

    public void Save(ISingletonSaver singletonSaver)
    {
        var saver = singletonSaver.GetSingleton(SavedKey);
        saver.Set(SavedAnchorIds, _namesByAnchor.Keys.ToArray());
        saver.Set(SavedNames, _namesByAnchor.Values.ToArray());
    }

    public bool TryGet(string anchorId, out string name)
        => _namesByAnchor.TryGetValue(anchorId, out name!);

    public void Set(string anchorId, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) { Remove(anchorId); return; }
        _namesByAnchor[anchorId] = name;
        Changed?.Invoke();
    }

    public void Remove(string anchorId)
    {
        if (_namesByAnchor.Remove(anchorId)) Changed?.Invoke();
    }

    public IReadOnlyDictionary<string, string> All => _namesByAnchor;
}

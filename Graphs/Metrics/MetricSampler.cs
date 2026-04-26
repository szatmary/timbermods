using System;
using System.Collections.Generic;
using System.Linq;
using Timberborn.GameDistricts;
using Timberborn.Persistence;
using Timberborn.SingletonSystem;
using Timberborn.TickSystem;
using Timberborn.TimeSystem;
using Timberborn.WorldPersistence;
using UnityEngine;

namespace Graphs.Metrics;

/// Samples every registered metric at a fixed cadence in in-game time.
/// Rate: 24 samples per in-game day (one per in-game hour).
///
/// Storage is per-district: each finished DistrictCenter (keyed by stable
/// EntityId) plus a "global" settlement-wide entry get their own tiered
/// history. The DistrictFilter selects which one the chart sees.
public sealed class MetricSampler : ILoadableSingleton, ITickableSingleton, ISaveableSingleton
{
    public const int SamplesPerDay = 24;

    /// Sentinel key in the per-district history map for the
    /// "no filter / all districts" history.
    public const string GlobalKey = "_global";

    // V3 — per-district tiered storage. Save uses runtime-built keys per
    // (district key × tier × field), so the singleton just stores a flat
    // list of district keys plus the metric ids to remap by.
    private static readonly SingletonKey SavedKey = new("GraphsMetricHistoryV3");
    private static readonly ListKey<string> SavedDistrictKeys = new("DistrictKeys");
    private static readonly ListKey<string> SavedMetricIds = new("MetricIds");

    // V2 — single-tier (one history) format. On load with no V3 present,
    // the V2 data becomes the global history. Per-district views start empty.
    private static readonly SingletonKey V2SavedKey = new("GraphsMetricHistoryV2");
    private static readonly ListKey<string> V2SavedMetricIds = new("MetricIds");
    private static readonly PropertyKey<int>  V2RecentCount      = new("RecentCount");
    private static readonly ListKey<float>    V2RecentValues     = new("RecentValues");
    private static readonly ListKey<float>    V2RecentTimestamps = new("RecentTimestamps");
    private static readonly ListKey<int>      V2RecentWeather    = new("RecentWeather");
    private static readonly PropertyKey<int>  V2MidCount         = new("MidCount");
    private static readonly ListKey<float>    V2MidValues        = new("MidValues");
    private static readonly ListKey<float>    V2MidTimestamps    = new("MidTimestamps");
    private static readonly ListKey<int>      V2MidWeather       = new("MidWeather");
    private static readonly PropertyKey<int>  V2OldCount         = new("OldCount");
    private static readonly ListKey<float>    V2OldValues        = new("OldValues");
    private static readonly ListKey<float>    V2OldTimestamps    = new("OldTimestamps");
    private static readonly ListKey<int>      V2OldWeather       = new("OldWeather");

    // V1 — flat single ring-buffer. Replayed through Append on load (so the
    // tiered structure populates organically).
    private static readonly SingletonKey V1SavedKey = new("GraphsMetricHistory");
    private static readonly PropertyKey<int> V1SavedSampleCount = new("SampleCount");
    private static readonly ListKey<string>  V1SavedMetricIds   = new("MetricIds");
    private static readonly ListKey<float>   V1SavedValues      = new("Values");
    private static readonly ListKey<float>   V1SavedTimestamps  = new("Timestamps");
    private static readonly ListKey<int>     V1SavedWeather     = new("Weather");

    private readonly IDayNightCycle _dayNightCycle;
    private readonly MetricRegistry _registry;
    private readonly DistrictFilter _filter;
    private readonly DistrictCenterRegistry _districts;
    private readonly WeatherStateSampler _weather;
    private readonly ISingletonLoader _singletonLoader;

    private readonly Dictionary<string, TieredMetricHistory> _histories = new();
    private int _lastSampleIndex = int.MinValue;
    private float[]? _scratch;
    private readonly HashSet<string> _loggedFailures = new();

    /// Recent (full-res) tier of the currently-selected district (or global).
    /// Used for "current value" reads in the legend.
    public MetricHistory Recent => GetOrCreate(CurrentDistrictKey()).Recent;

    /// Returns the tier whose resolution best matches the requested lookback,
    /// for the currently-selected district (or global).
    public MetricHistory HistoryFor(float lookbackDays)
        => GetOrCreate(CurrentDistrictKey()).HistoryFor(lookbackDays);

    public event Action? OnSampled;

    public MetricSampler(
        IDayNightCycle dayNightCycle,
        MetricRegistry registry,
        DistrictFilter filter,
        DistrictCenterRegistry districts,
        WeatherStateSampler weather,
        ISingletonLoader singletonLoader)
    {
        _dayNightCycle = dayNightCycle;
        _registry = registry;
        _filter = filter;
        _districts = districts;
        _weather = weather;
        _singletonLoader = singletonLoader;
    }

    public void Load()
    {
        _scratch = new float[_registry.Count];
        // Always have a global history present.
        GetOrCreate(GlobalKey);
        RestoreFromSave();
    }

    public void Tick()
    {
        if (_scratch is null) return;

        float partialDay = _dayNightCycle.DayNumber + _dayNightCycle.DayProgress;
        int sampleIndex = (int)Math.Floor(partialDay * SamplesPerDay);

        if (sampleIndex == _lastSampleIndex) return;
        _lastSampleIndex = sampleIndex;
        TakeSample(partialDay);
    }

    private string CurrentDistrictKey()
    {
        var id = _filter.DistrictId;
        return id?.ToString() ?? GlobalKey;
    }

    private TieredMetricHistory GetOrCreate(string key)
    {
        if (!_histories.TryGetValue(key, out var h))
        {
            h = new TieredMetricHistory(_registry.Count);
            _histories[key] = h;
        }
        return h;
    }

    private void TakeSample(float partialDay)
    {
        byte weather = _weather.Sample();

        // Global / "all districts" history — sample with null district.
        SampleInto(GetOrCreate(GlobalKey), district: null, weather, partialDay);

        // One history per finished district, keyed by stable entity id.
        foreach (var dc in _districts.FinishedDistrictCenters)
        {
            var id = DistrictFilter.GetEntityId(dc);
            if (id == null) continue;
            SampleInto(GetOrCreate(id.Value.ToString()), dc.DistrictName, weather, partialDay);
        }

        try { OnSampled?.Invoke(); }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Graphs] OnSampled handler threw: {ex.Message}");
        }
    }

    private void SampleInto(TieredMetricHistory h, string? district, byte weather, float partialDay)
    {
        var metrics = _registry.Metrics;
        for (int i = 0; i < metrics.Count; i++)
        {
            var def = metrics[i];
            try { _scratch![i] = def.ValueFn(district); }
            catch (Exception ex)
            {
                _scratch![i] = float.NaN;
                if (_loggedFailures.Add(def.Id))
                    Debug.LogWarning(
                        $"[Graphs] Metric '{def.Id}' threw on first sample (district={district ?? "<all>"}): {ex.Message}");
            }
        }
        h.Append(_scratch, weather, partialDay);
    }

    // ---------- persistence ----------

    public void Save(ISingletonSaver singletonSaver)
    {
        var saver = singletonSaver.GetSingleton(SavedKey);
        saver.Set(SavedMetricIds, _registry.Metrics.Select(m => m.Id).ToArray());
        var keys = _histories.Keys.ToArray();
        saver.Set(SavedDistrictKeys, keys);

        foreach (var key in keys)
        {
            var h = _histories[key];
            SaveTier(saver, h.Recent,
                new PropertyKey<int>($"R_{key}_C"),
                new ListKey<float>($"R_{key}_V"),
                new ListKey<float>($"R_{key}_T"),
                new ListKey<int>($"R_{key}_W"));
            SaveTier(saver, h.Mid,
                new PropertyKey<int>($"M_{key}_C"),
                new ListKey<float>($"M_{key}_V"),
                new ListKey<float>($"M_{key}_T"),
                new ListKey<int>($"M_{key}_W"));
            SaveTier(saver, h.Old,
                new PropertyKey<int>($"O_{key}_C"),
                new ListKey<float>($"O_{key}_V"),
                new ListKey<float>($"O_{key}_T"),
                new ListKey<int>($"O_{key}_W"));
        }
    }

    private static void SaveTier(
        IObjectSaver saver, MetricHistory tier,
        PropertyKey<int> countKey, ListKey<float> valuesKey,
        ListKey<float> timestampsKey, ListKey<int> weatherKey)
    {
        int n = tier.Count;
        int m = tier.MetricCount;
        if (n == 0)
        {
            saver.Set(countKey, 0);
            saver.Set(valuesKey, Array.Empty<float>());
            saver.Set(timestampsKey, Array.Empty<float>());
            saver.Set(weatherKey, Array.Empty<int>());
            return;
        }
        var values = new float[n * m];
        var timestamps = new float[n];
        var weather = new int[n];
        for (int i = 0; i < n; i++)
        {
            timestamps[i] = tier.ReadTimestamp(i);
            weather[i] = tier.ReadWeather(i);
            tier.ReadValues(i, new Span<float>(values, i * m, m));
        }
        saver.Set(countKey, n);
        saver.Set(valuesKey, values);
        saver.Set(timestampsKey, timestamps);
        saver.Set(weatherKey, weather);
    }

    private void RestoreFromSave()
    {
        if (_scratch is null) return;

        if (_singletonLoader.TryGetSingleton(SavedKey, out var v3Loader))
        {
            RestoreV3(v3Loader);
            return;
        }
        if (_singletonLoader.TryGetSingleton(V2SavedKey, out var v2Loader))
        {
            RestoreV2AsGlobal(v2Loader);
            return;
        }
        if (_singletonLoader.TryGetSingleton(V1SavedKey, out var v1Loader))
        {
            RestoreV1AsGlobal(v1Loader);
            return;
        }
    }

    private void RestoreV3(IObjectLoader loader)
    {
        List<string> savedMetricIds;
        List<string> savedDistrictKeys;
        try
        {
            savedMetricIds = loader.Get(SavedMetricIds);
            savedDistrictKeys = loader.Get(SavedDistrictKeys);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Graphs] V3 restore header failed: {ex.Message}");
            return;
        }
        if (savedMetricIds.Count == 0 || savedDistrictKeys.Count == 0) return;

        var savedToCurrent = new int[savedMetricIds.Count];
        for (int i = 0; i < savedMetricIds.Count; i++)
            savedToCurrent[i] = _registry.IndexOf(savedMetricIds[i]);

        int totalRestored = 0;
        float latestTimestamp = float.NegativeInfinity;
        foreach (var key in savedDistrictKeys)
        {
            var h = GetOrCreate(key);
            totalRestored += RestoreTier(loader, h.Recent, savedToCurrent,
                new PropertyKey<int>($"R_{key}_C"), new ListKey<float>($"R_{key}_V"),
                new ListKey<float>($"R_{key}_T"), new ListKey<int>($"R_{key}_W"),
                ref latestTimestamp);
            totalRestored += RestoreTier(loader, h.Mid, savedToCurrent,
                new PropertyKey<int>($"M_{key}_C"), new ListKey<float>($"M_{key}_V"),
                new ListKey<float>($"M_{key}_T"), new ListKey<int>($"M_{key}_W"),
                ref latestTimestamp);
            totalRestored += RestoreTier(loader, h.Old, savedToCurrent,
                new PropertyKey<int>($"O_{key}_C"), new ListKey<float>($"O_{key}_V"),
                new ListKey<float>($"O_{key}_T"), new ListKey<int>($"O_{key}_W"),
                ref latestTimestamp);
        }

        Debug.Log(
            $"[Graphs] V3 restore: {savedDistrictKeys.Count} district histories, " +
            $"{totalRestored} samples; latest timestamp {latestTimestamp:0.00}");

        if (latestTimestamp > float.NegativeInfinity)
            _lastSampleIndex = (int)Math.Floor(latestTimestamp * SamplesPerDay);
    }

    /// V2 had a single per-game history (no per-district split). Restore it
    /// into the global key; per-district views start empty.
    private void RestoreV2AsGlobal(IObjectLoader loader)
    {
        List<string> savedMetricIds;
        try { savedMetricIds = loader.Get(V2SavedMetricIds); }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Graphs] V2 restore header failed: {ex.Message}");
            return;
        }
        if (savedMetricIds.Count == 0) return;

        var savedToCurrent = new int[savedMetricIds.Count];
        for (int i = 0; i < savedMetricIds.Count; i++)
            savedToCurrent[i] = _registry.IndexOf(savedMetricIds[i]);

        var h = GetOrCreate(GlobalKey);
        int restored = 0;
        float latestTimestamp = float.NegativeInfinity;
        restored += RestoreTier(loader, h.Recent, savedToCurrent,
            V2RecentCount, V2RecentValues, V2RecentTimestamps, V2RecentWeather, ref latestTimestamp);
        restored += RestoreTier(loader, h.Mid, savedToCurrent,
            V2MidCount, V2MidValues, V2MidTimestamps, V2MidWeather, ref latestTimestamp);
        restored += RestoreTier(loader, h.Old, savedToCurrent,
            V2OldCount, V2OldValues, V2OldTimestamps, V2OldWeather, ref latestTimestamp);

        Debug.Log($"[Graphs] V2→V3 migration: restored {restored} samples to global; latest timestamp {latestTimestamp:0.00}");

        if (latestTimestamp > float.NegativeInfinity)
            _lastSampleIndex = (int)Math.Floor(latestTimestamp * SamplesPerDay);
    }

    /// V1 fallback: replay the flat single-buffer history through Append
    /// (into the global history) so it lands across the tiered structure.
    private void RestoreV1AsGlobal(IObjectLoader loader)
    {
        int sampleCount;
        List<string> savedMetricIds;
        List<float> values;
        List<float> timestamps;
        List<int> weather;
        try
        {
            sampleCount = loader.Get(V1SavedSampleCount);
            savedMetricIds = loader.Get(V1SavedMetricIds);
            values = loader.Get(V1SavedValues);
            timestamps = loader.Get(V1SavedTimestamps);
            weather = loader.Get(V1SavedWeather);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Graphs] V1 save restore failed: {ex.Message}");
            return;
        }
        if (sampleCount <= 0 || savedMetricIds.Count == 0) return;

        int savedMetricCount = savedMetricIds.Count;
        if (values.Count != sampleCount * savedMetricCount ||
            timestamps.Count != sampleCount ||
            weather.Count != sampleCount)
        {
            Debug.LogWarning("[Graphs] V1 saved history shape mismatch; discarding.");
            return;
        }

        var savedToCurrent = new int[savedMetricCount];
        for (int i = 0; i < savedMetricCount; i++)
            savedToCurrent[i] = _registry.IndexOf(savedMetricIds[i]);

        int currentMetricCount = _registry.Count;
        var row = new float[currentMetricCount];

        var globalHistory = GetOrCreate(GlobalKey);
        for (int s = 0; s < sampleCount; s++)
        {
            for (int m = 0; m < currentMetricCount; m++) row[m] = float.NaN;
            int baseIndex = s * savedMetricCount;
            for (int sm = 0; sm < savedMetricCount; sm++)
            {
                int cm = savedToCurrent[sm];
                if (cm >= 0 && cm < currentMetricCount)
                    row[cm] = values[baseIndex + sm];
            }
            globalHistory.Append(row, (byte)weather[s], timestamps[s]);
        }

        // Force-flush any partial Mid/Old buckets so the tail of the V1
        // samples isn't stranded in the in-memory accumulator.
        globalHistory.FlushPending();

        Debug.Log(
            $"[Graphs] V1→V3 migration: {sampleCount} samples → global; " +
            $"timestamps {timestamps[0]:0.00}..{timestamps[sampleCount - 1]:0.00}");

        _lastSampleIndex = (int)Math.Floor(timestamps[sampleCount - 1] * SamplesPerDay);
    }

    private int RestoreTier(
        IObjectLoader loader, MetricHistory tier, int[] savedToCurrent,
        PropertyKey<int> countKey, ListKey<float> valuesKey,
        ListKey<float> timestampsKey, ListKey<int> weatherKey,
        ref float latestTimestamp)
    {
        int sampleCount;
        List<float> values;
        List<float> timestamps;
        List<int> weather;
        try
        {
            sampleCount = loader.Get(countKey);
            values = loader.Get(valuesKey);
            timestamps = loader.Get(timestampsKey);
            weather = loader.Get(weatherKey);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Graphs] tier restore failed for '{countKey}': {ex.Message}");
            return 0;
        }
        if (sampleCount <= 0) return 0;

        int savedMetricCount = savedToCurrent.Length;
        if (values.Count != sampleCount * savedMetricCount ||
            timestamps.Count != sampleCount ||
            weather.Count != sampleCount)
        {
            Debug.LogWarning("[Graphs] tier shape mismatch; skipping tier.");
            return 0;
        }

        int currentMetricCount = _registry.Count;
        var row = new float[currentMetricCount];
        for (int s = 0; s < sampleCount; s++)
        {
            for (int m = 0; m < currentMetricCount; m++) row[m] = float.NaN;
            int baseIndex = s * savedMetricCount;
            for (int sm = 0; sm < savedMetricCount; sm++)
            {
                int cm = savedToCurrent[sm];
                if (cm >= 0 && cm < currentMetricCount)
                    row[cm] = values[baseIndex + sm];
            }
            tier.Append(row, (byte)weather[s], timestamps[s]);
        }
        if (timestamps[sampleCount - 1] > latestTimestamp)
            latestTimestamp = timestamps[sampleCount - 1];
        return sampleCount;
    }
}

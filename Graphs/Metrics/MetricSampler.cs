using System;
using System.Collections.Generic;
using System.Linq;
using Timberborn.Persistence;
using Timberborn.SingletonSystem;
using Timberborn.TickSystem;
using Timberborn.TimeSystem;
using Timberborn.WorldPersistence;
using UnityEngine;

namespace Graphs.Metrics;

/// Samples every registered metric at a fixed cadence in in-game time.
/// Rate: 24 samples per in-game day (one per in-game hour).
/// Stores into a three-tier history (see <see cref="TieredMetricHistory"/>):
/// recent samples at full res, older ones decimated to 4/day, oldest to 1/day.
public sealed class MetricSampler : ILoadableSingleton, ITickableSingleton, ISaveableSingleton
{
    public const int SamplesPerDay = 24;

    private static readonly SingletonKey SavedKey = new("GraphsMetricHistoryV2");
    private static readonly ListKey<string> SavedMetricIds = new("MetricIds");

    // V1 (single flat ring buffer) — read on load if V2 is absent so saves
    // from earlier mod versions still keep their history.
    private static readonly SingletonKey V1SavedKey = new("GraphsMetricHistory");
    private static readonly PropertyKey<int> V1SavedSampleCount = new("SampleCount");
    private static readonly ListKey<string>  V1SavedMetricIds   = new("MetricIds");
    private static readonly ListKey<float>   V1SavedValues      = new("Values");
    private static readonly ListKey<float>   V1SavedTimestamps  = new("Timestamps");
    private static readonly ListKey<int>     V1SavedWeather     = new("Weather");

    private static readonly PropertyKey<int>  RecentCount      = new("RecentCount");
    private static readonly ListKey<float>    RecentValues     = new("RecentValues");
    private static readonly ListKey<float>    RecentTimestamps = new("RecentTimestamps");
    private static readonly ListKey<int>      RecentWeather    = new("RecentWeather");

    private static readonly PropertyKey<int>  MidCount         = new("MidCount");
    private static readonly ListKey<float>    MidValues        = new("MidValues");
    private static readonly ListKey<float>    MidTimestamps    = new("MidTimestamps");
    private static readonly ListKey<int>      MidWeather       = new("MidWeather");

    private static readonly PropertyKey<int>  OldCount         = new("OldCount");
    private static readonly ListKey<float>    OldValues        = new("OldValues");
    private static readonly ListKey<float>    OldTimestamps    = new("OldTimestamps");
    private static readonly ListKey<int>      OldWeather       = new("OldWeather");

    private readonly IDayNightCycle _dayNightCycle;
    private readonly MetricRegistry _registry;
    private readonly DistrictFilter _filter;
    private readonly WeatherStateSampler _weather;
    private readonly ISingletonLoader _singletonLoader;

    private TieredMetricHistory? _history;
    private int _lastSampleIndex = int.MinValue;
    private float[]? _scratch;
    private readonly HashSet<string> _loggedFailures = new();

    /// Recent (full-res) tier — used for "current value" reads in the legend.
    public MetricHistory History
        => _history?.Recent ?? throw new InvalidOperationException("MetricSampler not loaded yet.");

    /// Returns the tier whose resolution best matches the requested lookback.
    public MetricHistory HistoryFor(float lookbackDays)
        => _history?.HistoryFor(lookbackDays)
            ?? throw new InvalidOperationException("MetricSampler not loaded yet.");

    public event Action? OnSampled;

    public MetricSampler(
        IDayNightCycle dayNightCycle,
        MetricRegistry registry,
        DistrictFilter filter,
        WeatherStateSampler weather,
        ISingletonLoader singletonLoader)
    {
        _dayNightCycle = dayNightCycle;
        _registry = registry;
        _filter = filter;
        _weather = weather;
        _singletonLoader = singletonLoader;
    }

    public void Load()
    {
        _history = new TieredMetricHistory(_registry.Count);
        _scratch = new float[_registry.Count];
        RestoreFromSave();
    }

    public void Tick()
    {
        if (_history is null || _scratch is null) return;

        float partialDay = _dayNightCycle.DayNumber + _dayNightCycle.DayProgress;
        int sampleIndex = (int)Math.Floor(partialDay * SamplesPerDay);

        if (sampleIndex == _lastSampleIndex) return;
        _lastSampleIndex = sampleIndex;
        TakeSample(partialDay);
    }

    private void TakeSample(float partialDay)
    {
        var metrics = _registry.Metrics;
        string? district = _filter.DistrictName;

        for (int i = 0; i < metrics.Count; i++)
        {
            var def = metrics[i];
            try { _scratch![i] = def.ValueFn(district); }
            catch (Exception ex)
            {
                _scratch![i] = float.NaN;
                if (_loggedFailures.Add(def.Id))
                    Debug.LogWarning(
                        $"[Graphs] Metric '{def.Id}' threw on first sample: {ex.Message}");
            }
        }

        byte weather = _weather.Sample();
        _history!.Append(_scratch, weather, partialDay);

        try { OnSampled?.Invoke(); }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Graphs] OnSampled handler threw: {ex.Message}");
        }
    }

    // ---------- persistence ----------

    public void Save(ISingletonSaver singletonSaver)
    {
        if (_history is null) return;

        var saver = singletonSaver.GetSingleton(SavedKey);
        saver.Set(SavedMetricIds, _registry.Metrics.Select(m => m.Id).ToArray());
        SaveTier(saver, _history.Recent, RecentCount, RecentValues, RecentTimestamps, RecentWeather);
        SaveTier(saver, _history.Mid,    MidCount,    MidValues,    MidTimestamps,    MidWeather);
        SaveTier(saver, _history.Old,    OldCount,    OldValues,    OldTimestamps,    OldWeather);
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
        if (_history is null || _scratch is null) return;

        if (_singletonLoader.TryGetSingleton(SavedKey, out var v2Loader))
        {
            RestoreV2(v2Loader);
            return;
        }
        if (_singletonLoader.TryGetSingleton(V1SavedKey, out var v1Loader))
        {
            RestoreV1(v1Loader);
            return;
        }
    }

    private void RestoreV2(IObjectLoader loader)
    {
        List<string> savedMetricIds;
        try { savedMetricIds = loader.Get(SavedMetricIds); }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Graphs] save restore failed (ignoring saved history): {ex.Message}");
            return;
        }
        if (savedMetricIds.Count == 0) return;

        var savedToCurrent = new int[savedMetricIds.Count];
        for (int i = 0; i < savedMetricIds.Count; i++)
            savedToCurrent[i] = _registry.IndexOf(savedMetricIds[i]);

        int restored = 0;
        float latestTimestamp = float.NegativeInfinity;
        restored += RestoreTier(loader, _history!.Recent, savedToCurrent,
            RecentCount, RecentValues, RecentTimestamps, RecentWeather, ref latestTimestamp);
        restored += RestoreTier(loader, _history.Mid, savedToCurrent,
            MidCount, MidValues, MidTimestamps, MidWeather, ref latestTimestamp);
        restored += RestoreTier(loader, _history.Old, savedToCurrent,
            OldCount, OldValues, OldTimestamps, OldWeather, ref latestTimestamp);

        Debug.Log($"[Graphs] restored {restored} samples across tiers; latest timestamp {latestTimestamp:0.00}");

        if (latestTimestamp > float.NegativeInfinity)
            _lastSampleIndex = (int)Math.Floor(latestTimestamp * SamplesPerDay);
    }

    /// V1 fallback: replay the flat single-buffer history through Append
    /// so each sample populates Recent (with overflow into Mid/Old via the
    /// running averages). The most-recent ~100 days end up in Recent at
    /// full res; older samples get decimated into Mid/Old as they're
    /// pushed through.
    private void RestoreV1(IObjectLoader loader)
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
            _history!.Append(row, (byte)weather[s], timestamps[s]);
        }

        Debug.Log(
            $"[Graphs] migrated {sampleCount} V1 samples into tiered history; " +
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
        catch { return 0; }
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

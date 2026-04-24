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
/// Rate: 240 samples per in-game day (one every 6 in-game minutes) — at
/// normal game speed that's ~2.5 real seconds between samples, so lines
/// start to appear almost immediately after opening the window.
/// Persists up to `PersistedSampleCap` samples into the save file, keyed
/// by metric id so a changed metric registry on reload remaps correctly.
public sealed class MetricSampler : ILoadableSingleton, ITickableSingleton, ISaveableSingleton
{
    public const int SamplesPerDay = 240;
    public const int MaxSamples = 48_000;      // ~200 in-game days of history.
    public const int PersistedSampleCap = 5000; // cap saved to ~20 in-game days.

    private static readonly SingletonKey SavedKey = new("GraphsMetricHistory");
    private static readonly PropertyKey<int> SavedSampleCount = new("SampleCount");
    private static readonly ListKey<string> SavedMetricIds = new("MetricIds");
    private static readonly ListKey<float> SavedValues = new("Values");
    private static readonly ListKey<float> SavedTimestamps = new("Timestamps");
    private static readonly ListKey<int> SavedWeather = new("Weather");

    private readonly IDayNightCycle _dayNightCycle;
    private readonly MetricRegistry _registry;
    private readonly DistrictFilter _filter;
    private readonly WeatherStateSampler _weather;
    private readonly ISingletonLoader _singletonLoader;

    private MetricHistory? _history;
    private int _lastSampleIndex = int.MinValue;
    private float[]? _scratch;
    private readonly HashSet<string> _loggedFailures = new();

    public MetricHistory History
        => _history ?? throw new InvalidOperationException("MetricSampler not loaded yet.");

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
        _history = new MetricHistory(MaxSamples, _registry.Count);
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

        int totalSamples = _history.Count;
        if (totalSamples == 0)
        {
            // Nothing to save — still write the singleton so Load() can detect
            // presence consistently.
            var emptySaver = singletonSaver.GetSingleton(SavedKey);
            emptySaver.Set(SavedSampleCount, 0);
            emptySaver.Set(SavedMetricIds, Array.Empty<string>());
            emptySaver.Set(SavedValues, Array.Empty<float>());
            emptySaver.Set(SavedTimestamps, Array.Empty<float>());
            emptySaver.Set(SavedWeather, Array.Empty<int>());
            return;
        }

        // Keep only the most-recent PersistedSampleCap samples.
        int take = Math.Min(totalSamples, PersistedSampleCap);
        int skip = totalSamples - take;

        int metricCount = _history.MetricCount;
        var metricIds = _registry.Metrics.Select(m => m.Id).ToArray();
        var values = new float[take * metricCount];
        var timestamps = new float[take];
        var weather = new int[take];

        for (int out_i = 0; out_i < take; out_i++)
        {
            int logical = skip + out_i;
            timestamps[out_i] = _history.ReadTimestamp(logical);
            weather[out_i] = _history.ReadWeather(logical);
            var dst = new Span<float>(values, out_i * metricCount, metricCount);
            _history.ReadValues(logical, dst);
        }

        var saver = singletonSaver.GetSingleton(SavedKey);
        saver.Set(SavedSampleCount, take);
        saver.Set(SavedMetricIds, metricIds);
        saver.Set(SavedValues, values);
        saver.Set(SavedTimestamps, timestamps);
        saver.Set(SavedWeather, weather);
    }

    private void RestoreFromSave()
    {
        if (_history is null || _scratch is null) return;
        if (!_singletonLoader.TryGetSingleton(SavedKey, out var loader)) return;

        int sampleCount;
        List<string> savedMetricIds;
        List<float> savedValues;
        List<float> savedTimestamps;
        List<int> savedWeather;
        try
        {
            sampleCount = loader.Get(SavedSampleCount);
            savedMetricIds = loader.Get(SavedMetricIds);
            savedValues = loader.Get(SavedValues);
            savedTimestamps = loader.Get(SavedTimestamps);
            savedWeather = loader.Get(SavedWeather);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Graphs] save restore failed (ignoring saved history): {ex.Message}");
            return;
        }

        if (sampleCount <= 0 || savedMetricIds.Count == 0) return;
        int savedMetricCount = savedMetricIds.Count;
        if (savedValues.Count != sampleCount * savedMetricCount ||
            savedTimestamps.Count != sampleCount ||
            savedWeather.Count != sampleCount)
        {
            Debug.LogWarning("[Graphs] saved history shape mismatch; discarding.");
            return;
        }

        // Build saved-index -> current-index map (or -1 if the metric id is
        // gone from the registry on this run).
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
                    row[cm] = savedValues[baseIndex + sm];
            }
            _history.Append(row, (byte)savedWeather[s], savedTimestamps[s]);
        }

        Debug.Log(
            $"[Graphs] restored {sampleCount} samples covering " +
            $"timestamps {savedTimestamps[0]:0.00}..{savedTimestamps[sampleCount - 1]:0.00}");

        // Seed _lastSampleIndex so we don't duplicate-sample the same slot on
        // the very next Tick immediately after load.
        float latestT = savedTimestamps[sampleCount - 1];
        _lastSampleIndex = (int)Math.Floor(latestT * SamplesPerDay);
    }
}

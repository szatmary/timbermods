using System;
using System.Collections.Generic;
using Timberborn.SingletonSystem;
using Timberborn.TickSystem;
using Timberborn.TimeSystem;
using UnityEngine;

namespace Graphs.Metrics;

/// Samples every registered metric at a fixed cadence in in-game time.
/// Rate: 240 samples per in-game day (one every 6 in-game minutes) — at
/// normal game speed that's ~2.5 real seconds between samples, so lines
/// start to appear almost immediately after opening the window.
public sealed class MetricSampler : ILoadableSingleton, ITickableSingleton
{
    public const int SamplesPerDay = 240;
    public const int MaxSamples = 48_000; // ~200 in-game days of history.

    private readonly IDayNightCycle _dayNightCycle;
    private readonly MetricRegistry _registry;
    private readonly DistrictFilter _filter;
    private readonly WeatherStateSampler _weather;

    private MetricHistory? _history;
    private int _lastSampleIndex = int.MinValue;
    private float[]? _scratch;

    // Metrics that have already logged a failure this session — so we log each once.
    private readonly HashSet<string> _loggedFailures = new();

    public MetricHistory History
        => _history ?? throw new InvalidOperationException("MetricSampler not loaded yet.");

    public event Action? OnSampled;

    public MetricSampler(
        IDayNightCycle dayNightCycle,
        MetricRegistry registry,
        DistrictFilter filter,
        WeatherStateSampler weather)
    {
        _dayNightCycle = dayNightCycle;
        _registry = registry;
        _filter = filter;
        _weather = weather;
    }

    public void Load()
    {
        _history = new MetricHistory(MaxSamples, _registry.Count);
        _scratch = new float[_registry.Count];
    }

    public void Tick()
    {
        if (_history is null || _scratch is null) return;

        // Synthetic continuous day count.
        float partialDay = _dayNightCycle.DayNumber + _dayNightCycle.DayProgress;
        int sampleIndex = (int)Math.Floor(partialDay * SamplesPerDay);

        if (sampleIndex == _lastSampleIndex) return;

        // Take one sample regardless of how many sample slots actually passed since
        // last tick — we don't try to backfill missed slots (e.g. after game speed up).
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
}

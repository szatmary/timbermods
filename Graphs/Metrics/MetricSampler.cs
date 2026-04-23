using System;
using System.Collections.Generic;
using Timberborn.SingletonSystem;
using Timberborn.TickSystem;
using Timberborn.TimeSystem;
using UnityEngine;

namespace Graphs.Metrics;

/// Samples every registered metric once per in-game hour.
public sealed class MetricSampler : ILoadableSingleton, ITickableSingleton
{
    public const int MaxSamples = 48_000; // 2000 days * 24 hours

    private readonly IDayNightCycle _dayNightCycle;
    private readonly MetricRegistry _registry;
    private readonly DistrictFilter _filter;
    private readonly WeatherStateSampler _weather;

    private MetricHistory? _history;
    private int _lastHourIndex = int.MinValue;
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
        int hourIndex = (int)Math.Floor(partialDay * 24f);

        if (hourIndex == _lastHourIndex) return;

        // Take one sample regardless of how many hours actually passed since
        // last tick — we don't try to backfill missed hours (e.g. after game speed up).
        _lastHourIndex = hourIndex;
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

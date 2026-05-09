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

namespace LogBook.Metrics;

/// Samples every registered metric 24 times per in-game day (one per hour).
/// Storage is per-district: each finished DistrictCenter (keyed by stable
/// EntityId) plus a settlement-wide "global" entry each have their own
/// tiered history. The DistrictFilter selects which one the chart reads.
/// Histories of destroyed districts are pruned.
public sealed class MetricSampler : ILoadableSingleton, ITickableSingleton, ISaveableSingleton
{
    public const int SamplesPerDay = 24;

    /// Sentinel key in the per-district history map for the
    /// "no filter / all districts" history.
    public const string GlobalKey = "_global";

    private static readonly SingletonKey SavedKey = new("LogBookMetricHistoryV3");
    private static readonly ListKey<string> SavedDistrictKeys = new("DistrictKeys");
    private static readonly ListKey<string> SavedMetricIds = new("MetricIds");

    /// Tier prefixes used in dynamically-built per-tier persistence keys.
    /// "R" = recent, "M" = mid, "O" = old. Append `_<districtKey>_<C|V|T|W>`.
    private static readonly string[] TierPrefixes = { "R", "M", "O" };

    private readonly IDayNightCycle _dayNightCycle;
    private readonly MetricRegistry _registry;
    private readonly DistrictFilter _filter;
    private readonly DistrictCenterRegistry _districts;
    private readonly WeatherStateSampler _weather;
    private readonly ISingletonLoader _singletonLoader;
    private readonly EventBus _eventBus;

    private readonly Dictionary<string, TieredMetricHistory> _histories = new();
    private int _lastSampleIndex = int.MinValue;
    private float[]? _scratch;
    private readonly HashSet<string> _loggedFailures = new();

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
        ISingletonLoader singletonLoader,
        EventBus eventBus)
    {
        _dayNightCycle = dayNightCycle;
        _registry = registry;
        _filter = filter;
        _districts = districts;
        _weather = weather;
        _singletonLoader = singletonLoader;
        _eventBus = eventBus;
    }

    public void Load()
    {
        _scratch = new float[_registry.Count];
        GetOrCreate(GlobalKey);
        RestoreFromSave();
        _eventBus.Register(this);
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

    /// Drop histories whose district has been removed. Keeps the global
    /// key. Prevents unbounded growth of saved data.
    [OnEvent]
    public void OnDistrictCenterRegistryChanged(DistrictCenterRegistryChangedEvent _)
    {
        var live = new HashSet<string> { GlobalKey };
        foreach (var dc in _districts.AllDistrictCenters)
        {
            var id = DistrictFilter.GetEntityId(dc);
            if (id != null) live.Add(id.Value.ToString());
        }
        var stale = _histories.Keys.Where(k => !live.Contains(k)).ToList();
        foreach (var k in stale) _histories.Remove(k);
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
        SampleInto(GetOrCreate(GlobalKey), district: null, weather, partialDay);
        foreach (var dc in _districts.FinishedDistrictCenters)
        {
            var id = DistrictFilter.GetEntityId(dc);
            if (id == null) continue;
            SampleInto(GetOrCreate(id.Value.ToString()), dc.DistrictName, weather, partialDay);
        }
        OnSampled?.Invoke();
    }

    private void SampleInto(TieredMetricHistory h, string? district, byte weather, float partialDay)
    {
        var metrics = _registry.Metrics;
        for (int i = 0; i < metrics.Count; i++)
        {
            var def = metrics[i];
            // ValueFn can throw transiently (e.g. a building destroyed
            // between samples). Store NaN so the chart shows a gap, and
            // log once per metric id so the warning isn't spammy.
            try { _scratch![i] = def.ValueFn(district); }
            catch (Exception ex)
            {
                _scratch![i] = float.NaN;
                if (_loggedFailures.Add(def.Id))
                    Debug.LogWarning(
                        $"[LogBook] Metric '{def.Id}' threw on first sample (district={district ?? "<all>"}): {ex.Message}");
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
            var tiers = new[] { h.Recent, h.Mid, h.Old };
            for (int i = 0; i < TierPrefixes.Length; i++)
                SaveTier(saver, tiers[i], TierKeyPrefix(TierPrefixes[i], key));
        }
    }

    private static string TierKeyPrefix(string tierLetter, string districtKey)
        => $"{tierLetter}_{districtKey}_";

    private static void SaveTier(IObjectSaver saver, MetricHistory tier, string prefix)
    {
        int n = tier.Count;
        int m = tier.MetricCount;
        var countKey = new PropertyKey<int>(prefix + "C");
        var valuesKey = new ListKey<float>(prefix + "V");
        var timestampsKey = new ListKey<float>(prefix + "T");
        var weatherKey = new ListKey<int>(prefix + "W");
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
        if (!_singletonLoader.TryGetSingleton(SavedKey, out var loader)) return;

        var savedMetricIds = loader.Get(SavedMetricIds);
        var savedDistrictKeys = loader.Get(SavedDistrictKeys);
        if (savedMetricIds.Count == 0 || savedDistrictKeys.Count == 0) return;

        var savedToCurrent = new int[savedMetricIds.Count];
        for (int i = 0; i < savedMetricIds.Count; i++)
            savedToCurrent[i] = _registry.IndexOf(savedMetricIds[i]);

        int totalRestored = 0;
        float latestTimestamp = float.NegativeInfinity;
        foreach (var key in savedDistrictKeys)
        {
            var h = GetOrCreate(key);
            var tiers = new[] { h.Recent, h.Mid, h.Old };
            for (int i = 0; i < TierPrefixes.Length; i++)
                totalRestored += RestoreTier(loader, tiers[i],
                    TierKeyPrefix(TierPrefixes[i], key),
                    savedToCurrent, ref latestTimestamp);
        }

        Debug.Log(
            $"[LogBook] restore: {savedDistrictKeys.Count} district histories, " +
            $"{totalRestored} samples; latest timestamp {latestTimestamp:0.00}");

        if (latestTimestamp > float.NegativeInfinity)
            _lastSampleIndex = (int)Math.Floor(latestTimestamp * SamplesPerDay);
    }

    private int RestoreTier(
        IObjectLoader loader, MetricHistory tier, string prefix,
        int[] savedToCurrent, ref float latestTimestamp)
    {
        int sampleCount = loader.Get(new PropertyKey<int>(prefix + "C"));
        if (sampleCount <= 0) return 0;
        var values = loader.Get(new ListKey<float>(prefix + "V"));
        var timestamps = loader.Get(new ListKey<float>(prefix + "T"));
        var weather = loader.Get(new ListKey<int>(prefix + "W"));

        int savedMetricCount = savedToCurrent.Length;
        if (values.Count != sampleCount * savedMetricCount ||
            timestamps.Count != sampleCount ||
            weather.Count != sampleCount)
        {
            throw new InvalidOperationException(
                $"[LogBook] tier shape mismatch under prefix '{prefix}': " +
                $"sampleCount={sampleCount}, values={values.Count}, " +
                $"timestamps={timestamps.Count}, weather={weather.Count}");
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

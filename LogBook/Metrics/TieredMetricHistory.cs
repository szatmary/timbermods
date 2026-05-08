using System;

namespace Graphs.Metrics;

/// Three-tier history: Recent (24/day, last 100d), Mid (4/day, last 1000d),
/// Old (1/day, last 10000d). Each Append goes to Recent at full resolution
/// and is accumulated into Mid/Old running averages; once N raw samples have
/// accumulated for a tier, the average is flushed to that tier's ring buffer
/// with a timestamp at the bucket's midpoint.
///
/// Charts pick a tier via HistoryFor(lookbackDays). The chosen tier must
/// actually reach further back than the next-finer tier; otherwise we use
/// the finer tier (which has at least as much coverage at higher resolution).
public sealed class TieredMetricHistory
{
    public const int RecentCapacity = 2_400;   // 100 days @ 24/day
    public const int MidCapacity    = 4_000;   // 1000 days @ 4/day
    public const int OldCapacity    = 10_000;  // 10000 days @ 1/day

    public const int MidDecimation = 6;        // 6 raw samples → 1 mid sample
    public const int OldDecimation = 24;       // 24 raw samples → 1 old sample

    private readonly int _metricCount;
    private readonly MetricHistory _recent;
    private readonly MetricHistory _mid;
    private readonly MetricHistory _old;

    // Running sums + counters for tier averaging. Weather is decimated by
    // taking the most-recent raw weather value at flush time (not averaged).
    // Timestamps: track first + last in each bucket so we can stamp the
    // flushed sample at the bucket midpoint instead of its trailing edge.
    private readonly double[] _midSum;
    private readonly double[] _oldSum;
    private int _midCount;
    private int _oldCount;
    private byte _midLastWeather;
    private byte _oldLastWeather;
    private float _midFirstTimestamp;
    private float _oldFirstTimestamp;
    private float _midLastTimestamp;
    private float _oldLastTimestamp;

    internal MetricHistory Recent => _recent;
    internal MetricHistory Mid => _mid;
    internal MetricHistory Old => _old;

    public TieredMetricHistory(int metricCount)
    {
        _metricCount = metricCount;
        _recent = new MetricHistory(RecentCapacity, metricCount);
        _mid    = new MetricHistory(MidCapacity, metricCount);
        _old    = new MetricHistory(OldCapacity, metricCount);
        _midSum = new double[metricCount];
        _oldSum = new double[metricCount];
    }

    public void Append(ReadOnlySpan<float> values, byte weather, float timestamp)
    {
        if (values.Length != _metricCount)
            throw new ArgumentException(
                $"Expected {_metricCount} values, got {values.Length}.", nameof(values));

        _recent.Append(values, weather, timestamp);

        // Accumulate into mid + old running sums. A single NaN in a bucket
        // poisons the average; the resulting NaN renders as a gap on the
        // chart, which is the desired behavior.
        if (_midCount == 0) _midFirstTimestamp = timestamp;
        if (_oldCount == 0) _oldFirstTimestamp = timestamp;
        for (int i = 0; i < _metricCount; i++) _midSum[i] += values[i];
        for (int i = 0; i < _metricCount; i++) _oldSum[i] += values[i];
        _midCount++;
        _oldCount++;
        _midLastWeather = weather;
        _oldLastWeather = weather;
        _midLastTimestamp = timestamp;
        _oldLastTimestamp = timestamp;

        if (_midCount >= MidDecimation) FlushMid();
        if (_oldCount >= OldDecimation) FlushOld();
    }

    private void FlushMid()
    {
        var avg = new float[_metricCount];
        double inv = 1.0 / _midCount;
        for (int i = 0; i < _metricCount; i++) avg[i] = (float)(_midSum[i] * inv);
        float midpoint = (_midFirstTimestamp + _midLastTimestamp) * 0.5f;
        _mid.Append(avg, _midLastWeather, midpoint);
        Array.Clear(_midSum, 0, _metricCount);
        _midCount = 0;
    }

    private void FlushOld()
    {
        var avg = new float[_metricCount];
        double inv = 1.0 / _oldCount;
        for (int i = 0; i < _metricCount; i++) avg[i] = (float)(_oldSum[i] * inv);
        float midpoint = (_oldFirstTimestamp + _oldLastTimestamp) * 0.5f;
        _old.Append(avg, _oldLastWeather, midpoint);
        Array.Clear(_oldSum, 0, _metricCount);
        _oldCount = 0;
    }

    /// Pick the tier whose resolution best fits the requested lookback.
    /// A coarser tier wins only when it actually reaches further back than
    /// the next-finer tier — otherwise the finer tier has at least as much
    /// coverage at higher resolution and should be used.
    public MetricHistory HistoryFor(float lookbackDays)
    {
        if (lookbackDays <= 100f) return _recent;

        if (lookbackDays <= 1000f)
            return OldestTimestamp(_mid) < OldestTimestamp(_recent) ? _mid : _recent;

        // lookbackDays > 1000
        var midOrRecent = OldestTimestamp(_mid) < OldestTimestamp(_recent) ? _mid : _recent;
        return OldestTimestamp(_old) < OldestTimestamp(midOrRecent) ? _old : midOrRecent;
    }

    private static float OldestTimestamp(MetricHistory h)
        => h.Count == 0 ? float.PositiveInfinity : h.ReadTimestamp(0);
}

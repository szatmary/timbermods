using System;

namespace LogBook.Metrics;

/// Fixed-capacity ring buffer of metric samples.
/// Layout: values is a [capacity][metricCount] 2D layout stored in a single
/// flat float array for cache locality. Weather and timestamps are parallel arrays.
public sealed class MetricHistory
{
    public const byte WeatherTemperate = 0;
    public const byte WeatherDrought = 1;
    public const byte WeatherBadtide = 2;

    private readonly int _capacity;
    private readonly int _metricCount;
    private readonly float[] _values;        // length = capacity * metricCount
    private readonly byte[] _weather;        // length = capacity
    private readonly float[] _timestamps;    // length = capacity (DaysSinceStart at sample time)

    private int _head;    // index of the next write slot
    private int _count;   // number of valid samples (caps at _capacity)

    public int MetricCount => _metricCount;
    public int Count => _count;

    public MetricHistory(int capacity, int metricCount)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        if (metricCount < 0) throw new ArgumentOutOfRangeException(nameof(metricCount));
        _capacity = capacity;
        _metricCount = metricCount;
        _values = new float[capacity * metricCount];
        _weather = new byte[capacity];
        _timestamps = new float[capacity];
    }

    /// Append a new sample. `values.Length` must equal `MetricCount`.
    public void Append(ReadOnlySpan<float> values, byte weather, float timestamp)
    {
        if (values.Length != _metricCount)
            throw new ArgumentException(
                $"Expected {_metricCount} values, got {values.Length}.", nameof(values));

        int offset = _head * _metricCount;
        values.CopyTo(new Span<float>(_values, offset, _metricCount));
        _weather[_head] = weather;
        _timestamps[_head] = timestamp;

        _head = (_head + 1) % _capacity;
        if (_count < _capacity) _count++;
    }

    /// Returns the physical slot index for a logical index (0 = oldest).
    private int PhysicalIndex(int logicalIndex)
    {
        if ((uint)logicalIndex >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(logicalIndex));
        int start = _count < _capacity ? 0 : _head;
        return (start + logicalIndex) % _capacity;
    }

    /// Read sample values at a logical index (0 = oldest).
    public void ReadValues(int logicalIndex, Span<float> dest)
    {
        if (dest.Length != _metricCount)
            throw new ArgumentException("dest size mismatch");
        int phys = PhysicalIndex(logicalIndex);
        new ReadOnlySpan<float>(_values, phys * _metricCount, _metricCount).CopyTo(dest);
    }

    public float ReadValue(int logicalIndex, int metricIndex)
    {
        int phys = PhysicalIndex(logicalIndex);
        return _values[phys * _metricCount + metricIndex];
    }

    public byte ReadWeather(int logicalIndex) => _weather[PhysicalIndex(logicalIndex)];
    public float ReadTimestamp(int logicalIndex) => _timestamps[PhysicalIndex(logicalIndex)];

    /// Returns the lowest logical index whose timestamp >= threshold, or
    /// Count if all samples are older. Timestamps are monotonic so binary
    /// search applies.
    public int FindFirstAtOrAfter(float timestamp)
    {
        int lo = 0, hi = _count;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (ReadTimestamp(mid) < timestamp) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }
}

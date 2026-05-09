using System;
using Timberborn.GameCycleSystem;
using Timberborn.HazardousWeatherSystem;
using Timberborn.WeatherSystem;

namespace LogBook.Metrics;

/// Returns the current weather state for each sample. The hazardous-cycle
/// service tells us *which* hazard is scheduled this cycle; comparing
/// CycleDay against HazardousWeatherStartCycleDay tells us whether it's
/// currently active, so temperate days before a drought aren't tinted.
public sealed class WeatherStateSampler
{
    private readonly HazardousWeatherService _hazardous;
    private readonly WeatherService _weather;
    private readonly GameCycleService _cycle;

    public WeatherStateSampler(
        HazardousWeatherService hazardous,
        WeatherService weather,
        GameCycleService cycle)
    {
        _hazardous = hazardous;
        _weather = weather;
        _cycle = cycle;
    }

    public byte Sample()
    {
        var hw = _hazardous.CurrentCycleHazardousWeather;
        if (hw == null) return MetricHistory.WeatherTemperate;
        if (_cycle.CycleDay < _weather.HazardousWeatherStartCycleDay)
            return MetricHistory.WeatherTemperate;

        string name = hw.GetType().Name;
        if (name.IndexOf("Badtide", StringComparison.OrdinalIgnoreCase) >= 0)
            return MetricHistory.WeatherBadtide;
        if (name.IndexOf("Drought", StringComparison.OrdinalIgnoreCase) >= 0)
            return MetricHistory.WeatherDrought;
        return MetricHistory.WeatherTemperate;
    }
}

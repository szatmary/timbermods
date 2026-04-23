using System;
using Timberborn.GameCycleSystem;
using Timberborn.HazardousWeatherSystem;
using Timberborn.WeatherSystem;
using UnityEngine;

namespace Graphs.Metrics;

/// Returns the current weather state for each sample. Uses
/// `GameCycleService.CycleDay` vs `TemperateWeatherDurationService.HazardousWeatherStartCycleDay`
/// to tell if the hazardous period of the cycle is active right now — so
/// temperate days before a drought don't get tinted with the drought color.
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
        try
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
        catch (Exception ex)
        {
            Debug.LogWarning($"[Graphs] Weather sample failed: {ex.Message}");
            return MetricHistory.WeatherTemperate;
        }
    }
}

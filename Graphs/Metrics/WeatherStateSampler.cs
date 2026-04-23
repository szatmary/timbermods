using System;
using Timberborn.GameCycleSystem;
using Timberborn.HazardousWeatherSystem;
using UnityEngine;

namespace Graphs.Metrics;

/// Returns the current weather state for each sample. Uses
/// `GameCycleService.CycleDay` vs `HazardousWeatherStartCycleDay` to tell
/// if the hazardous period of the cycle is active right now — so temperate
/// days before a drought don't get tinted with the drought color.
public sealed class WeatherStateSampler
{
    private readonly HazardousWeatherService _weather;
    private readonly GameCycleService _cycle;

    public WeatherStateSampler(HazardousWeatherService weather, GameCycleService cycle)
    {
        _weather = weather;
        _cycle = cycle;
    }

    public byte Sample()
    {
        try
        {
            var hw = _weather.CurrentCycleHazardousWeather;
            if (hw == null) return MetricHistory.WeatherTemperate;

            // The cycle schedules a hazardous weather but the first part of
            // the cycle is still temperate. Only return the hazardous color
            // once we've crossed the start day.
            if (_cycle.CycleDay < _cycle.HazardousWeatherStartCycleDay)
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

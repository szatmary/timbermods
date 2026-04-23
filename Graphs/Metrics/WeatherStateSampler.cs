using System;
using Timberborn.HazardousWeatherSystem;
using UnityEngine;

namespace Graphs.Metrics;

/// Resolves the active weather state for a sample. Isolated so that the
/// exact game-API binding lives in one place — if the weather API shape
/// changes across game versions, only this class needs updating.
public sealed class WeatherStateSampler
{
    private readonly HazardousWeatherService _weather;

    public WeatherStateSampler(HazardousWeatherService weather)
    {
        _weather = weather;
    }

    /// Returns one of MetricHistory.Weather{Temperate,Drought,Badtide}.
    public byte Sample()
    {
        try
        {
            var hw = _weather.CurrentCycleHazardousWeather;
            if (hw == null) return MetricHistory.WeatherTemperate;
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

using System;

namespace SeoulZikimi.Weather
{
    public enum Season
    {
        Spring,
        Summer,
        Autumn,
        Winter
    }

    public enum SeasonSelectionMode
    {
        Fixed,
        Random
    }

    public enum WeatherKind
    {
        Sunny,
        Rain,
        Snow,
        StrongWind,
        Typhoon,
        AutumnLeaves,
        CherryBlossom
    }

    /// <summary>
    /// 세션 생성 화면에서 전달할 계절/날씨 옵션이다.
    /// 네트워크나 UI 구현에 의존하지 않으므로 어느 계층에서도 만들 수 있다.
    /// </summary>
    public sealed class WeatherSessionOptions
    {
        public bool IsEnabled { get; }
        public SeasonSelectionMode SeasonSelection { get; }
        public Season FixedSeason { get; }

        public WeatherSessionOptions(
            bool isEnabled,
            SeasonSelectionMode seasonSelection,
            Season fixedSeason = Season.Spring)
        {
            IsEnabled = isEnabled;
            SeasonSelection = seasonSelection;
            FixedSeason = fixedSeason;
        }
    }

    public readonly struct WeatherSelection
    {
        public bool IsEnabled { get; }
        public Season Season { get; }
        public WeatherKind Weather { get; }

        private WeatherSelection(bool isEnabled, Season season, WeatherKind weather)
        {
            IsEnabled = isEnabled;
            Season = season;
            Weather = weather;
        }

        public static WeatherSelection Disabled()
            => new WeatherSelection(false, default, default);

        public static WeatherSelection Enabled(Season season, WeatherKind weather)
            => new WeatherSelection(true, season, weather);
    }

    public readonly struct WeightedWeather
    {
        public WeatherKind Weather { get; }
        public float Weight { get; }

        public WeightedWeather(WeatherKind weather, float weight)
        {
            if (weight <= 0f)
                throw new ArgumentOutOfRangeException(nameof(weight), "가중치는 0보다 커야 합니다.");

            Weather = weather;
            Weight = weight;
        }
    }
}

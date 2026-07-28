using System;
using System.Collections.Generic;

namespace SeoulZikimi.Weather
{
    public sealed class SystemRandomSource : IRandomSource
    {
        private readonly Random _random;

        public SystemRandomSource(int? seed = null)
        {
            _random = seed.HasValue ? new Random(seed.Value) : new Random();
        }

        public float NextFloat() => (float)_random.NextDouble();

        public int NextInt(int maxExclusive)
        {
            if (maxExclusive <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxExclusive));

            return _random.Next(maxExclusive);
        }
    }

    public sealed class SeasonWeatherTable
    {
        private readonly IReadOnlyDictionary<Season, IReadOnlyList<WeightedWeather>> _table;

        public SeasonWeatherTable(
            IReadOnlyDictionary<Season, IReadOnlyList<WeightedWeather>> table)
        {
            _table = table ?? throw new ArgumentNullException(nameof(table));

            foreach (Season season in Enum.GetValues(typeof(Season)))
            {
                if (!_table.TryGetValue(season, out var entries) || entries == null || entries.Count == 0)
                    throw new ArgumentException($"{season} 계절의 날씨 확률이 필요합니다.", nameof(table));
            }
        }

        public IReadOnlyList<WeightedWeather> Get(Season season) => _table[season];

        public static SeasonWeatherTable CreateDefault()
        {
            return new SeasonWeatherTable(
                new Dictionary<Season, IReadOnlyList<WeightedWeather>>
                {
                    [Season.Spring] = new[]
                    {
                        new WeightedWeather(WeatherKind.Sunny, 60f),
                        new WeightedWeather(WeatherKind.Rain, 10f),
                        new WeightedWeather(WeatherKind.CherryBlossom, 30f)
                    },
                    [Season.Summer] = new[]
                    {
                        new WeightedWeather(WeatherKind.Sunny, 50f),
                        new WeightedWeather(WeatherKind.Rain, 35f),
                        new WeightedWeather(WeatherKind.Typhoon, 15f)
                    },
                    [Season.Autumn] = new[]
                    {
                        new WeightedWeather(WeatherKind.Sunny, 60f),
                        new WeightedWeather(WeatherKind.Rain, 10f),
                        new WeightedWeather(WeatherKind.AutumnLeaves, 30f)
                    },
                    [Season.Winter] = new[]
                    {
                        new WeightedWeather(WeatherKind.Sunny, 50f),
                        new WeightedWeather(WeatherKind.Snow, 35f),
                        new WeightedWeather(WeatherKind.StrongWind, 15f)
                    }
                });
        }
    }

    public sealed class WeightedWeatherSelector : IWeatherSelector
    {
        private static readonly Season[] Seasons =
        {
            Season.Spring,
            Season.Summer,
            Season.Autumn,
            Season.Winter
        };

        private readonly SeasonWeatherTable _table;
        private readonly IRandomSource _random;

        public WeightedWeatherSelector(SeasonWeatherTable table, IRandomSource random)
        {
            _table = table ?? throw new ArgumentNullException(nameof(table));
            _random = random ?? throw new ArgumentNullException(nameof(random));
        }

        public WeatherSelection Select(WeatherSessionOptions options)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));

            if (!options.IsEnabled)
                return WeatherSelection.Disabled();

            Season season = options.SeasonSelection == SeasonSelectionMode.Random
                ? Seasons[_random.NextInt(Seasons.Length)]
                : options.FixedSeason;

            return WeatherSelection.Enabled(season, SelectWeather(season));
        }

        private WeatherKind SelectWeather(Season season)
        {
            IReadOnlyList<WeightedWeather> entries = _table.Get(season);
            float totalWeight = 0f;

            for (int i = 0; i < entries.Count; i++)
                totalWeight += entries[i].Weight;

            float roll = _random.NextFloat() * totalWeight;
            float cumulativeWeight = 0f;

            for (int i = 0; i < entries.Count; i++)
            {
                cumulativeWeight += entries[i].Weight;
                if (roll < cumulativeWeight)
                    return entries[i].Weather;
            }

            // NextFloat 구현의 부동소수점 오차에 대한 안전장치다.
            return entries[entries.Count - 1].Weather;
        }
    }
}

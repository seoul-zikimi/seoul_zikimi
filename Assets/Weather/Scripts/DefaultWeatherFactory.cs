using System;
using System.Collections.Generic;

namespace SeoulZikimi.Weather
{
    public static class DefaultWeatherFactory
    {
        /// <summary>
        /// 기획서의 기본 확률과 효과를 조립한다.
        /// 실제 월드, 바람 방향, VFX 구현은 호출하는 쪽에서 주입한다.
        /// </summary>
        public static WeatherController Create(
            IWeatherWorld world,
            IWindDirectionProvider windDirection,
            IWeatherVisualPresenter visualPresenter,
            IRandomSource random = null)
        {
            if (world == null)
                throw new ArgumentNullException(nameof(world));
            if (windDirection == null)
                throw new ArgumentNullException(nameof(windDirection));
            if (visualPresenter == null)
                throw new ArgumentNullException(nameof(visualPresenter));

            random ??= new SystemRandomSource();

            IWeatherEffect noGameplayEffect = new NoGameplayWeatherEffect();
            IWeatherEffect rainEffect = new SlipWeatherEffect(random, 0.1f);
            IWeatherEffect snowEffect = new SlipWeatherEffect(random, 0.1f);
            IWeatherEffect windEffect = CreateWindEffect(world, windDirection, random);
            IWeatherEffect typhoonEffect = new CompositeWeatherEffect(
                new SlipWeatherEffect(random, 0.1f),
                CreateWindEffect(world, windDirection, random));

            var catalog = new WeatherCatalog(
                new Dictionary<WeatherKind, IWeatherEffect>
                {
                    [WeatherKind.Sunny] = noGameplayEffect,
                    [WeatherKind.Rain] = rainEffect,
                    [WeatherKind.Snow] = snowEffect,
                    [WeatherKind.StrongWind] = windEffect,
                    [WeatherKind.Typhoon] = typhoonEffect,
                    [WeatherKind.AutumnLeaves] = noGameplayEffect,
                    [WeatherKind.CherryBlossom] = noGameplayEffect
                });

            var selector = new WeightedWeatherSelector(
                SeasonWeatherTable.CreateDefault(),
                random);

            return new WeatherController(selector, catalog, visualPresenter);
        }

        private static IWeatherEffect CreateWindEffect(
            IWeatherWorld world,
            IWindDirectionProvider windDirection,
            IRandomSource random)
        {
            return new StrongWindWeatherEffect(
                world,
                windDirection,
                random,
                moveSpeed: 0.1f,
                dropInterval: 15f);
        }
    }
}

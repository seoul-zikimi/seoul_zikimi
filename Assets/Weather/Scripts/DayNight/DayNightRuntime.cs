using System;

namespace SeoulZikimi.Weather
{
    /// <summary>
    /// 낮/밤 선택과 각 표현 계층을 조율한다.
    /// 구체적인 Light, Material, GameObject에는 의존하지 않는다.
    /// </summary>
    public sealed class DayNightController
    {
        private readonly ITimeOfDaySelector _selector;
        private readonly ITimeOfDayProfileCatalog _profiles;
        private readonly ITimeOfDayLightingPresenter _lighting;
        private readonly ITimeOfDaySkyboxPresenter _skybox;
        private readonly ITimeOfDaySceneryPresenter _scenery;

        public TimeOfDaySelection Current { get; private set; }
        public bool IsRunning => Current.IsEnabled;

        public DayNightController(
            ITimeOfDaySelector selector,
            ITimeOfDayProfileCatalog profiles,
            ITimeOfDayLightingPresenter lighting,
            ITimeOfDaySkyboxPresenter skybox,
            ITimeOfDaySceneryPresenter scenery)
        {
            _selector = selector ?? throw new ArgumentNullException(nameof(selector));
            _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
            _lighting = lighting ?? throw new ArgumentNullException(nameof(lighting));
            _skybox = skybox ?? throw new ArgumentNullException(nameof(skybox));
            _scenery = scenery ?? throw new ArgumentNullException(nameof(scenery));
            Current = TimeOfDaySelection.Disabled();
        }

        public TimeOfDaySelection Start(DayNightSessionOptions options)
        {
            Stop();
            Current = _selector.Select(options);

            if (!Current.IsEnabled)
                return Current;

            TimeOfDayVisualProfile profile = _profiles.GetProfile(Current.TimeOfDay);
            _lighting.ApplyLighting(profile);
            _skybox.ApplySkybox(profile);
            _scenery.ApplyScenery(profile);
            return Current;
        }

        public void Stop()
        {
            _scenery.ResetScenery();
            _skybox.ResetSkybox();
            _lighting.ResetLighting();
            Current = TimeOfDaySelection.Disabled();
        }
    }

    /// <summary>날씨와 낮/밤을 세션 환경 하나로 다루는 상위 진입점이다.</summary>
    public sealed class WorldEnvironmentController
    {
        private readonly WeatherController _weather;
        private readonly DayNightController _dayNight;

        public WorldEnvironmentController(
            WeatherController weather,
            DayNightController dayNight)
        {
            _weather = weather ?? throw new ArgumentNullException(nameof(weather));
            _dayNight = dayNight ?? throw new ArgumentNullException(nameof(dayNight));
        }

        public EnvironmentSelection Start(
            WeatherSessionOptions weatherOptions,
            DayNightSessionOptions dayNightOptions)
        {
            WeatherSelection weather = _weather.Start(weatherOptions);
            TimeOfDaySelection timeOfDay = _dayNight.Start(dayNightOptions);
            return new EnvironmentSelection(weather, timeOfDay);
        }

        public void Tick(float deltaTime)
        {
            _weather.Tick(deltaTime);
        }

        public void NotifyActorMoved(IWeatherActor actor)
        {
            _weather.NotifyActorMoved(actor);
        }

        public void Stop()
        {
            _weather.Stop();
            _dayNight.Stop();
        }
    }
}

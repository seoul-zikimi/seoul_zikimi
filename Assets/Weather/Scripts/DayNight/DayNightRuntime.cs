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

        /// <summary>
        /// 날씨와 낮/밤 선택이 확정된 직후 발생한다.
        /// 인게임 계절 아이콘이나 입장 안내 UI가 결과를 표시할 때 구독한다.
        /// </summary>
        public event Action<EnvironmentSelection> EnvironmentStarted;

        /// <summary>환경 효과가 종료되어 인게임 환경 안내 UI를 숨겨야 할 때 발생한다.</summary>
        public event Action EnvironmentStopped;

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
            var selection = new EnvironmentSelection(weather, timeOfDay);
            EnvironmentStarted?.Invoke(selection);
            return selection;
        }

        /// <summary>
        /// 방 생성 UI에서 만든 통합 옵션으로 날씨와 낮/밤 시스템을 함께 시작한다.
        /// </summary>
        public EnvironmentSelection Start(EnvironmentSessionOptions options)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));

            return Start(options.Weather, options.DayNight);
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
            EnvironmentStopped?.Invoke();
        }
    }
}

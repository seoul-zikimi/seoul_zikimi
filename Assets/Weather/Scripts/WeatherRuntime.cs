using System;
using System.Collections.Generic;

namespace SeoulZikimi.Weather
{
    public sealed class WeatherCatalog : IWeatherCatalog
    {
        private readonly IReadOnlyDictionary<WeatherKind, IWeatherEffect> _effects;

        public WeatherCatalog(IReadOnlyDictionary<WeatherKind, IWeatherEffect> effects)
        {
            _effects = effects ?? throw new ArgumentNullException(nameof(effects));

            foreach (WeatherKind weather in Enum.GetValues(typeof(WeatherKind)))
            {
                if (!_effects.TryGetValue(weather, out var effect) || effect == null)
                    throw new ArgumentException($"{weather} 날씨의 효과가 필요합니다.", nameof(effects));
            }
        }

        public IWeatherEffect GetEffect(WeatherKind weather) => _effects[weather];
    }

    /// <summary>
    /// 세션 옵션 적용, 현재 효과 수명주기, VFX 통지를 조율하는 진입점이다.
    /// MonoBehaviour가 아니므로 원하는 게임 루프나 네트워크 계층에서 소유하면 된다.
    /// </summary>
    public sealed class WeatherController
    {
        private readonly IWeatherSelector _selector;
        private readonly IWeatherCatalog _catalog;
        private readonly IWeatherVisualPresenter _visualPresenter;
        private IWeatherEffect _activeEffect;

        public WeatherSelection Current { get; private set; }
        public bool IsRunning => Current.IsEnabled && _activeEffect != null;

        public WeatherController(
            IWeatherSelector selector,
            IWeatherCatalog catalog,
            IWeatherVisualPresenter visualPresenter)
        {
            _selector = selector ?? throw new ArgumentNullException(nameof(selector));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _visualPresenter = visualPresenter ?? throw new ArgumentNullException(nameof(visualPresenter));
            Current = WeatherSelection.Disabled();
        }

        public WeatherSelection Start(WeatherSessionOptions options)
        {
            Stop();
            Current = _selector.Select(options);

            if (!Current.IsEnabled)
                return Current;

            _activeEffect = _catalog.GetEffect(Current.Weather);
            _activeEffect.Enter();
            _visualPresenter.Show(Current.Season, Current.Weather);
            return Current;
        }

        public void Stop()
        {
            if (_activeEffect != null)
            {
                _activeEffect.Exit();
                _activeEffect = null;
            }

            _visualPresenter.Hide();
            Current = WeatherSelection.Disabled();
        }

        public void Tick(float deltaTime)
        {
            _activeEffect?.Tick(deltaTime);
        }

        /// <summary>
        /// 이동 판정 주기마다 호출한다. 호출 빈도 자체는 Player 어댑터에서 결정한다.
        /// </summary>
        public void NotifyActorMoved(IWeatherActor actor)
        {
            _activeEffect?.OnActorMoved(actor);
        }
    }
}

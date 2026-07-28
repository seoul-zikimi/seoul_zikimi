using System;
using System.Collections.Generic;
using UnityEngine;

namespace SeoulZikimi.Weather
{
    /// <summary>
    /// 직접 제작할 UI에 붙이는 로직 전용 브리지다.
    /// 시각 컴포넌트를 참조하지 않으며 공개 함수를 UI 이벤트에 연결하면 된다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnvironmentSettingsUI :
        MonoBehaviour,
        IEnvironmentSettingsSource
    {
        [Header("초기 날씨 설정")]
        [SerializeField] private bool m_WeatherEnabled = true;
        [SerializeField] private SeasonSelectionMode m_SeasonSelectionMode = SeasonSelectionMode.Random;
        [SerializeField] private Season m_FixedSeason = Season.Spring;

        [Header("초기 낮/밤 설정")]
        [SerializeField] private bool m_DayNightEnabled = true;
        [SerializeField] private TimeOfDaySelectionMode m_TimeOfDaySelectionMode = TimeOfDaySelectionMode.Random;
        [SerializeField] private TimeOfDay m_FixedTimeOfDay = TimeOfDay.Day;

        private EnvironmentSettingsViewModel _viewModel;

        public EnvironmentSettingsState Current
        {
            get
            {
                EnsureInitialized();
                return _viewModel.Current;
            }
        }

        public event Action<EnvironmentSettingsState> StateChanged;

        private void Awake()
        {
            EnsureInitialized();
        }

        private void OnDestroy()
        {
            if (_viewModel != null)
                _viewModel.StateChanged -= ForwardStateChanged;
        }

        /// <summary>
        /// 현재 값을 바꾸지 않고 StateChanged를 다시 발생시킨다.
        /// UI가 처음 열렸을 때 토글, 선택 표시, 하위 패널 활성 상태를 한 번에 갱신하는 용도다.
        /// </summary>
        public void Refresh()
        {
            EnsureInitialized();
            _viewModel.Refresh();
        }

        /// <summary>
        /// '사계절과 날씨 사용' Toggle.onValueChanged(bool)에 연결한다.
        /// false가 되면 계절 선택 UI는 숨길 수 있지만 기존 선택값은 보존된다.
        /// </summary>
        public void OnWeatherEnabledChanged(bool isEnabled)
        {
            EnsureInitialized();
            _viewModel.SetWeatherEnabled(isEnabled);
        }

        /// <summary>별도 '사용' 버튼을 만들었을 때 Button.onClick에 연결한다.</summary>
        public void EnableWeather() => OnWeatherEnabledChanged(true);

        /// <summary>별도 '사용 안 함' 버튼을 만들었을 때 Button.onClick에 연결한다.</summary>
        public void DisableWeather() => OnWeatherEnabledChanged(false);

        /// <summary>
        /// '계절 랜덤 선택' Toggle.onValueChanged(bool)에 연결한다.
        /// true는 랜덤, false는 사용자가 고른 계절을 사용한다.
        /// </summary>
        public void OnRandomSeasonChanged(bool useRandom)
        {
            EnsureInitialized();
            _viewModel.SetSeasonSelectionMode(
                useRandom ? SeasonSelectionMode.Random : SeasonSelectionMode.Fixed);
        }

        /// <summary>'랜덤 계절' Button.onClick에 연결한다.</summary>
        public void UseRandomSeason() => OnRandomSeasonChanged(true);

        /// <summary>'계절 직접 선택' Button.onClick에 연결한다.</summary>
        public void UseFixedSeason() => OnRandomSeasonChanged(false);

        /// <summary>
        /// 계절 Dropdown.onValueChanged(int)에 연결한다.
        /// 옵션 순서는 반드시 봄(0), 여름(1), 가을(2), 겨울(3)이어야 한다.
        /// </summary>
        public void OnSeasonDropdownChanged(int seasonIndex)
        {
            EnsureInitialized();
            _viewModel.SetFixedSeason(ToDefinedEnum<Season>(seasonIndex, nameof(seasonIndex)));
        }

        /// <summary>봄 전용 Button.onClick에 연결한다.</summary>
        public void SelectSpring() => SelectSeason(Season.Spring);

        /// <summary>여름 전용 Button.onClick에 연결한다.</summary>
        public void SelectSummer() => SelectSeason(Season.Summer);

        /// <summary>가을 전용 Button.onClick에 연결한다.</summary>
        public void SelectAutumn() => SelectSeason(Season.Autumn);

        /// <summary>겨울 전용 Button.onClick에 연결한다.</summary>
        public void SelectWinter() => SelectSeason(Season.Winter);

        /// <summary>
        /// '낮/밤 전경 사용' Toggle.onValueChanged(bool)에 연결한다.
        /// false가 되면 낮/밤 선택 UI는 숨길 수 있지만 기존 선택값은 보존된다.
        /// </summary>
        public void OnDayNightEnabledChanged(bool isEnabled)
        {
            EnsureInitialized();
            _viewModel.SetDayNightEnabled(isEnabled);
        }

        /// <summary>별도 '낮/밤 사용' Button.onClick에 연결한다.</summary>
        public void EnableDayNight() => OnDayNightEnabledChanged(true);

        /// <summary>별도 '낮/밤 사용 안 함' Button.onClick에 연결한다.</summary>
        public void DisableDayNight() => OnDayNightEnabledChanged(false);

        /// <summary>
        /// '낮/밤 랜덤 선택' Toggle.onValueChanged(bool)에 연결한다.
        /// true는 랜덤, false는 사용자가 고른 낮 또는 밤을 사용한다.
        /// </summary>
        public void OnRandomTimeOfDayChanged(bool useRandom)
        {
            EnsureInitialized();
            _viewModel.SetTimeOfDaySelectionMode(
                useRandom ? TimeOfDaySelectionMode.Random : TimeOfDaySelectionMode.Fixed);
        }

        /// <summary>'낮/밤 랜덤' Button.onClick에 연결한다.</summary>
        public void UseRandomTimeOfDay() => OnRandomTimeOfDayChanged(true);

        /// <summary>'낮/밤 직접 선택' Button.onClick에 연결한다.</summary>
        public void UseFixedTimeOfDay() => OnRandomTimeOfDayChanged(false);

        /// <summary>
        /// 낮/밤 Dropdown.onValueChanged(int)에 연결한다.
        /// 옵션 순서는 반드시 낮(0), 밤(1)이어야 한다.
        /// </summary>
        public void OnTimeOfDayDropdownChanged(int timeOfDayIndex)
        {
            EnsureInitialized();
            _viewModel.SetFixedTimeOfDay(
                ToDefinedEnum<TimeOfDay>(timeOfDayIndex, nameof(timeOfDayIndex)));
        }

        /// <summary>낮 전용 Button.onClick에 연결한다.</summary>
        public void SelectDay() => SelectTimeOfDay(TimeOfDay.Day);

        /// <summary>밤 전용 Button.onClick에 연결한다.</summary>
        public void SelectNight() => SelectTimeOfDay(TimeOfDay.Night);

        /// <summary>
        /// 현재 직접 선택된 계절의 날씨 종류와 가중치를 반환한다.
        /// 확률 안내 문구나 막대그래프를 만들 때 사용한다.
        /// </summary>
        public IReadOnlyList<WeightedWeather> GetSelectedSeasonWeatherChances()
        {
            EnsureInitialized();
            return _viewModel.GetSelectedSeasonWeatherChances();
        }

        /// <summary>
        /// 현재 UI 값을 게임 로직에서 사용하는 세션 옵션으로 변환한다.
        /// 방 생성이 확정되는 시점에 한 번 호출해 WorldEnvironmentController.Start에 전달한다.
        /// </summary>
        public EnvironmentSessionOptions BuildSessionOptions()
        {
            EnsureInitialized();
            return _viewModel.BuildSessionOptions();
        }

        private void SelectSeason(Season season)
        {
            EnsureInitialized();
            _viewModel.SetFixedSeason(season);
        }

        private void SelectTimeOfDay(TimeOfDay timeOfDay)
        {
            EnsureInitialized();
            _viewModel.SetFixedTimeOfDay(timeOfDay);
        }

        private void EnsureInitialized()
        {
            if (_viewModel != null)
                return;

            _viewModel = new EnvironmentSettingsViewModel(
                new EnvironmentSettingsState(
                    m_WeatherEnabled,
                    m_SeasonSelectionMode,
                    m_FixedSeason,
                    m_DayNightEnabled,
                    m_TimeOfDaySelectionMode,
                    m_FixedTimeOfDay));
            _viewModel.StateChanged += ForwardStateChanged;
        }

        private void ForwardStateChanged(EnvironmentSettingsState state)
            => StateChanged?.Invoke(state);

        private static TEnum ToDefinedEnum<TEnum>(int value, string parameterName)
            where TEnum : struct, Enum
        {
            if (!Enum.IsDefined(typeof(TEnum), value))
                throw new ArgumentOutOfRangeException(parameterName, value, "지원하지 않는 UI 인덱스입니다.");

            return (TEnum)Enum.ToObject(typeof(TEnum), value);
        }
    }
}

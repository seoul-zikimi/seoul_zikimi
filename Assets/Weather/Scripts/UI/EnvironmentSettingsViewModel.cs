using System;
using System.Collections.Generic;

namespace SeoulZikimi.Weather
{
    /// <summary>
    /// 환경 설정 UI의 실제 로직이다.
    /// 화면 구현은 StateChanged를 받아 표시만 갱신하면 된다.
    /// </summary>
    public sealed class EnvironmentSettingsViewModel : IEnvironmentSettingsSource
    {
        private readonly SeasonWeatherTable _weatherTable;

        public EnvironmentSettingsState Current { get; private set; }
        public event Action<EnvironmentSettingsState> StateChanged;

        public EnvironmentSettingsViewModel(
            EnvironmentSettingsState initialState,
            SeasonWeatherTable weatherTable = null)
        {
            ValidateSeason(initialState.FixedSeason);
            ValidateSeasonSelectionMode(initialState.SeasonSelectionMode);
            ValidateTimeOfDay(initialState.FixedTimeOfDay);
            ValidateTimeOfDaySelectionMode(initialState.TimeOfDaySelectionMode);
            _weatherTable = weatherTable ?? SeasonWeatherTable.CreateDefault();
            Current = initialState;
        }

        public static EnvironmentSettingsViewModel CreateDefault()
        {
            return new EnvironmentSettingsViewModel(
                new EnvironmentSettingsState(
                    isWeatherEnabled: true,
                    seasonSelectionMode: SeasonSelectionMode.Random,
                    fixedSeason: Season.Spring,
                    isDayNightEnabled: true,
                    timeOfDaySelectionMode: TimeOfDaySelectionMode.Random,
                    fixedTimeOfDay: TimeOfDay.Day));
        }

        /// <summary>사계절과 날씨 시스템의 적용 여부를 변경한다.</summary>
        public void SetWeatherEnabled(bool isEnabled)
            => SetState(
                isEnabled,
                Current.SeasonSelectionMode,
                Current.FixedSeason,
                Current.IsDayNightEnabled,
                Current.TimeOfDaySelectionMode,
                Current.FixedTimeOfDay);

        /// <summary>계절을 직접 선택할지 세션 시작 시 무작위로 뽑을지 변경한다.</summary>
        public void SetSeasonSelectionMode(SeasonSelectionMode mode)
        {
            ValidateSeasonSelectionMode(mode);
            SetState(
                Current.IsWeatherEnabled,
                mode,
                Current.FixedSeason,
                Current.IsDayNightEnabled,
                Current.TimeOfDaySelectionMode,
                Current.FixedTimeOfDay);
        }

        /// <summary>계절 직접 선택 모드에서 사용할 계절을 저장한다.</summary>
        public void SetFixedSeason(Season season)
        {
            ValidateSeason(season);
            SetState(
                Current.IsWeatherEnabled,
                Current.SeasonSelectionMode,
                season,
                Current.IsDayNightEnabled,
                Current.TimeOfDaySelectionMode,
                Current.FixedTimeOfDay);
        }

        /// <summary>낮/밤 전경 시스템의 적용 여부를 변경한다.</summary>
        public void SetDayNightEnabled(bool isEnabled)
            => SetState(
                Current.IsWeatherEnabled,
                Current.SeasonSelectionMode,
                Current.FixedSeason,
                isEnabled,
                Current.TimeOfDaySelectionMode,
                Current.FixedTimeOfDay);

        /// <summary>낮/밤을 직접 선택할지 세션 시작 시 무작위로 뽑을지 변경한다.</summary>
        public void SetTimeOfDaySelectionMode(TimeOfDaySelectionMode mode)
        {
            ValidateTimeOfDaySelectionMode(mode);
            SetState(
                Current.IsWeatherEnabled,
                Current.SeasonSelectionMode,
                Current.FixedSeason,
                Current.IsDayNightEnabled,
                mode,
                Current.FixedTimeOfDay);
        }

        /// <summary>낮/밤 직접 선택 모드에서 사용할 시간대를 저장한다.</summary>
        public void SetFixedTimeOfDay(TimeOfDay timeOfDay)
        {
            ValidateTimeOfDay(timeOfDay);
            SetState(
                Current.IsWeatherEnabled,
                Current.SeasonSelectionMode,
                Current.FixedSeason,
                Current.IsDayNightEnabled,
                Current.TimeOfDaySelectionMode,
                timeOfDay);
        }

        /// <summary>현재 선택한 계절에 표시할 날씨 확률표 데이터를 반환한다.</summary>
        public IReadOnlyList<WeightedWeather> GetSelectedSeasonWeatherChances()
            => _weatherTable.Get(Current.FixedSeason);

        /// <summary>현재 UI 상태의 복사본을 게임 실행용 환경 옵션으로 만든다.</summary>
        public EnvironmentSessionOptions BuildSessionOptions()
        {
            return new EnvironmentSessionOptions(
                new WeatherSessionOptions(
                    Current.IsWeatherEnabled,
                    Current.SeasonSelectionMode,
                    Current.FixedSeason),
                new DayNightSessionOptions(
                    Current.IsDayNightEnabled,
                    Current.TimeOfDaySelectionMode,
                    Current.FixedTimeOfDay));
        }

        /// <summary>값 변경 없이 현재 상태를 UI에 다시 알린다.</summary>
        public void Refresh()
            => StateChanged?.Invoke(Current);

        private void SetState(
            bool isWeatherEnabled,
            SeasonSelectionMode seasonSelectionMode,
            Season fixedSeason,
            bool isDayNightEnabled,
            TimeOfDaySelectionMode timeOfDaySelectionMode,
            TimeOfDay fixedTimeOfDay)
        {
            var next = new EnvironmentSettingsState(
                isWeatherEnabled,
                seasonSelectionMode,
                fixedSeason,
                isDayNightEnabled,
                timeOfDaySelectionMode,
                fixedTimeOfDay);

            if (next.Equals(Current))
                return;

            Current = next;
            StateChanged?.Invoke(Current);
        }

        private static void ValidateSeason(Season season)
        {
            if (!Enum.IsDefined(typeof(Season), season))
                throw new ArgumentOutOfRangeException(nameof(season));
        }

        private static void ValidateSeasonSelectionMode(SeasonSelectionMode mode)
        {
            if (!Enum.IsDefined(typeof(SeasonSelectionMode), mode))
                throw new ArgumentOutOfRangeException(nameof(mode));
        }

        private static void ValidateTimeOfDay(TimeOfDay timeOfDay)
        {
            if (!Enum.IsDefined(typeof(TimeOfDay), timeOfDay))
                throw new ArgumentOutOfRangeException(nameof(timeOfDay));
        }

        private static void ValidateTimeOfDaySelectionMode(TimeOfDaySelectionMode mode)
        {
            if (!Enum.IsDefined(typeof(TimeOfDaySelectionMode), mode))
                throw new ArgumentOutOfRangeException(nameof(mode));
        }
    }
}

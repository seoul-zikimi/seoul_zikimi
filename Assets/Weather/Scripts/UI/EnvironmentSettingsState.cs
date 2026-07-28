using System;

namespace SeoulZikimi.Weather
{
    /// <summary>
    /// UI가 화면을 그리는 데 필요한 값만 담은 읽기 전용 상태다.
    /// 특정 Button, Toggle, Dropdown 구현에는 의존하지 않는다.
    /// </summary>
    public readonly struct EnvironmentSettingsState : IEquatable<EnvironmentSettingsState>
    {
        public bool IsWeatherEnabled { get; }
        public SeasonSelectionMode SeasonSelectionMode { get; }
        public Season FixedSeason { get; }
        public bool IsDayNightEnabled { get; }
        public TimeOfDaySelectionMode TimeOfDaySelectionMode { get; }
        public TimeOfDay FixedTimeOfDay { get; }

        public bool ShowSeasonControls => IsWeatherEnabled;
        public bool ShowFixedSeasonControls =>
            IsWeatherEnabled && SeasonSelectionMode == SeasonSelectionMode.Fixed;
        public bool ShowTimeOfDayControls => IsDayNightEnabled;
        public bool ShowFixedTimeOfDayControls =>
            IsDayNightEnabled && TimeOfDaySelectionMode == TimeOfDaySelectionMode.Fixed;

        public EnvironmentSettingsState(
            bool isWeatherEnabled,
            SeasonSelectionMode seasonSelectionMode,
            Season fixedSeason,
            bool isDayNightEnabled,
            TimeOfDaySelectionMode timeOfDaySelectionMode,
            TimeOfDay fixedTimeOfDay)
        {
            IsWeatherEnabled = isWeatherEnabled;
            SeasonSelectionMode = seasonSelectionMode;
            FixedSeason = fixedSeason;
            IsDayNightEnabled = isDayNightEnabled;
            TimeOfDaySelectionMode = timeOfDaySelectionMode;
            FixedTimeOfDay = fixedTimeOfDay;
        }

        public bool Equals(EnvironmentSettingsState other)
        {
            return IsWeatherEnabled == other.IsWeatherEnabled
                && SeasonSelectionMode == other.SeasonSelectionMode
                && FixedSeason == other.FixedSeason
                && IsDayNightEnabled == other.IsDayNightEnabled
                && TimeOfDaySelectionMode == other.TimeOfDaySelectionMode
                && FixedTimeOfDay == other.FixedTimeOfDay;
        }

        public override bool Equals(object obj)
            => obj is EnvironmentSettingsState other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = IsWeatherEnabled.GetHashCode();
                hash = (hash * 397) ^ (int)SeasonSelectionMode;
                hash = (hash * 397) ^ (int)FixedSeason;
                hash = (hash * 397) ^ IsDayNightEnabled.GetHashCode();
                hash = (hash * 397) ^ (int)TimeOfDaySelectionMode;
                hash = (hash * 397) ^ (int)FixedTimeOfDay;
                return hash;
            }
        }
    }

    public interface IEnvironmentSettingsSource
    {
        EnvironmentSettingsState Current { get; }
        event Action<EnvironmentSettingsState> StateChanged;
        EnvironmentSessionOptions BuildSessionOptions();
    }
}

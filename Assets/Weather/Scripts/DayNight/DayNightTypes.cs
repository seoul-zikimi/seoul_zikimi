using System;

namespace SeoulZikimi.Weather
{
    public enum TimeOfDay
    {
        Day,
        Night
    }

    public enum TimeOfDaySelectionMode
    {
        Fixed,
        Random
    }

    /// <summary>세션 생성 화면에서 전달할 낮/밤 옵션이다.</summary>
    public sealed class DayNightSessionOptions
    {
        public bool IsEnabled { get; }
        public TimeOfDaySelectionMode SelectionMode { get; }
        public TimeOfDay FixedTimeOfDay { get; }

        public DayNightSessionOptions(
            bool isEnabled,
            TimeOfDaySelectionMode selectionMode,
            TimeOfDay fixedTimeOfDay = TimeOfDay.Day)
        {
            IsEnabled = isEnabled;
            SelectionMode = selectionMode;
            FixedTimeOfDay = fixedTimeOfDay;
        }
    }

    public readonly struct TimeOfDaySelection
    {
        public bool IsEnabled { get; }
        public TimeOfDay TimeOfDay { get; }

        private TimeOfDaySelection(bool isEnabled, TimeOfDay timeOfDay)
        {
            IsEnabled = isEnabled;
            TimeOfDay = timeOfDay;
        }

        public static TimeOfDaySelection Disabled()
            => new TimeOfDaySelection(false, default);

        public static TimeOfDaySelection Enabled(TimeOfDay timeOfDay)
            => new TimeOfDaySelection(true, timeOfDay);
    }

    /// <summary>
    /// 실제 에셋을 직접 들지 않는 낮/밤 표현 설정이다.
    /// 각 맵의 Presenter가 키를 해당 맵의 스카이박스와 전경 오브젝트로 해석한다.
    /// </summary>
    public sealed class TimeOfDayVisualProfile
    {
        public TimeOfDay TimeOfDay { get; }
        public string SkyboxVariantKey { get; }
        public string SceneryVariantKey { get; }
        public float LightIntensityMultiplier { get; }
        public float TransitionDuration { get; }

        public TimeOfDayVisualProfile(
            TimeOfDay timeOfDay,
            string skyboxVariantKey,
            string sceneryVariantKey,
            float lightIntensityMultiplier,
            float transitionDuration)
        {
            if (string.IsNullOrWhiteSpace(skyboxVariantKey))
                throw new ArgumentException("스카이박스 변형 키가 필요합니다.", nameof(skyboxVariantKey));
            if (string.IsNullOrWhiteSpace(sceneryVariantKey))
                throw new ArgumentException("맵 전경 변형 키가 필요합니다.", nameof(sceneryVariantKey));
            if (lightIntensityMultiplier < 0f)
                throw new ArgumentOutOfRangeException(nameof(lightIntensityMultiplier));
            if (transitionDuration < 0f)
                throw new ArgumentOutOfRangeException(nameof(transitionDuration));

            TimeOfDay = timeOfDay;
            SkyboxVariantKey = skyboxVariantKey;
            SceneryVariantKey = sceneryVariantKey;
            LightIntensityMultiplier = lightIntensityMultiplier;
            TransitionDuration = transitionDuration;
        }
    }

    public readonly struct EnvironmentSelection
    {
        public WeatherSelection Weather { get; }
        public TimeOfDaySelection TimeOfDay { get; }

        public EnvironmentSelection(
            WeatherSelection weather,
            TimeOfDaySelection timeOfDay)
        {
            Weather = weather;
            TimeOfDay = timeOfDay;
        }
    }
}

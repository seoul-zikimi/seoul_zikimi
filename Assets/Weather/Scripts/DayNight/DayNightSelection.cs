using System;
using System.Collections.Generic;

namespace SeoulZikimi.Weather
{
    public sealed class RandomTimeOfDaySelector : ITimeOfDaySelector
    {
        private static readonly TimeOfDay[] Times =
        {
            TimeOfDay.Day,
            TimeOfDay.Night
        };

        private readonly IRandomSource _random;

        public RandomTimeOfDaySelector(IRandomSource random)
        {
            _random = random ?? throw new ArgumentNullException(nameof(random));
        }

        public TimeOfDaySelection Select(DayNightSessionOptions options)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));

            if (!options.IsEnabled)
                return TimeOfDaySelection.Disabled();

            TimeOfDay selected = options.SelectionMode == TimeOfDaySelectionMode.Random
                ? Times[_random.NextInt(Times.Length)]
                : options.FixedTimeOfDay;

            return TimeOfDaySelection.Enabled(selected);
        }
    }

    public sealed class TimeOfDayProfileCatalog : ITimeOfDayProfileCatalog
    {
        private readonly IReadOnlyDictionary<TimeOfDay, TimeOfDayVisualProfile> _profiles;

        public TimeOfDayProfileCatalog(
            IReadOnlyDictionary<TimeOfDay, TimeOfDayVisualProfile> profiles)
        {
            _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));

            foreach (TimeOfDay timeOfDay in Enum.GetValues(typeof(TimeOfDay)))
            {
                if (!_profiles.TryGetValue(timeOfDay, out var profile) || profile == null)
                    throw new ArgumentException($"{timeOfDay} 시각 프로필이 필요합니다.", nameof(profiles));

                if (profile.TimeOfDay != timeOfDay)
                    throw new ArgumentException($"{timeOfDay} 키와 프로필 시간이 일치하지 않습니다.", nameof(profiles));
            }
        }

        public TimeOfDayVisualProfile GetProfile(TimeOfDay timeOfDay)
            => _profiles[timeOfDay];

        public static TimeOfDayProfileCatalog CreateDefault()
        {
            return new TimeOfDayProfileCatalog(
                new Dictionary<TimeOfDay, TimeOfDayVisualProfile>
                {
                    [TimeOfDay.Day] = new TimeOfDayVisualProfile(
                        TimeOfDay.Day,
                        skyboxVariantKey: "Day",
                        sceneryVariantKey: "Day",
                        lightIntensityMultiplier: 1f,
                        transitionDuration: 1.5f),
                    [TimeOfDay.Night] = new TimeOfDayVisualProfile(
                        TimeOfDay.Night,
                        skyboxVariantKey: "Night",
                        sceneryVariantKey: "Night",
                        lightIntensityMultiplier: 0.35f,
                        transitionDuration: 1.5f)
                });
        }
    }
}

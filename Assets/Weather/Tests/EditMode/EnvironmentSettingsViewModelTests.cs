using NUnit.Framework;

namespace SeoulZikimi.Weather.Tests
{
    public sealed class EnvironmentSettingsViewModelTests
    {
        [Test]
        public void ChangedState_NotifiesViewOnce()
        {
            EnvironmentSettingsViewModel viewModel = EnvironmentSettingsViewModel.CreateDefault();
            int notificationCount = 0;
            viewModel.StateChanged += _ => notificationCount++;

            viewModel.SetWeatherEnabled(false);
            viewModel.SetWeatherEnabled(false);

            Assert.That(notificationCount, Is.EqualTo(1));
            Assert.That(viewModel.Current.ShowSeasonControls, Is.False);
        }

        [Test]
        public void FixedSelection_ShowsOnlyRelevantControls()
        {
            EnvironmentSettingsViewModel viewModel = EnvironmentSettingsViewModel.CreateDefault();

            viewModel.SetSeasonSelectionMode(SeasonSelectionMode.Fixed);
            viewModel.SetTimeOfDaySelectionMode(TimeOfDaySelectionMode.Fixed);

            Assert.That(viewModel.Current.ShowFixedSeasonControls, Is.True);
            Assert.That(viewModel.Current.ShowFixedTimeOfDayControls, Is.True);
        }

        [Test]
        public void BuildSessionOptions_CopiesCurrentUiState()
        {
            EnvironmentSettingsViewModel viewModel = EnvironmentSettingsViewModel.CreateDefault();
            viewModel.SetFixedSeason(Season.Winter);
            viewModel.SetSeasonSelectionMode(SeasonSelectionMode.Fixed);
            viewModel.SetFixedTimeOfDay(TimeOfDay.Night);
            viewModel.SetTimeOfDaySelectionMode(TimeOfDaySelectionMode.Fixed);

            EnvironmentSessionOptions options = viewModel.BuildSessionOptions();

            Assert.That(options.Weather.FixedSeason, Is.EqualTo(Season.Winter));
            Assert.That(options.Weather.SeasonSelection, Is.EqualTo(SeasonSelectionMode.Fixed));
            Assert.That(options.DayNight.FixedTimeOfDay, Is.EqualTo(TimeOfDay.Night));
            Assert.That(options.DayNight.SelectionMode, Is.EqualTo(TimeOfDaySelectionMode.Fixed));
        }

        [Test]
        public void SelectedSeasonChance_ReturnsWinterPlan()
        {
            EnvironmentSettingsViewModel viewModel = EnvironmentSettingsViewModel.CreateDefault();
            viewModel.SetFixedSeason(Season.Winter);

            var chances = viewModel.GetSelectedSeasonWeatherChances();

            Assert.That(chances.Count, Is.EqualTo(3));
            Assert.That(chances[0].Weather, Is.EqualTo(WeatherKind.Sunny));
            Assert.That(chances[0].Weight, Is.EqualTo(50f));
            Assert.That(chances[1].Weather, Is.EqualTo(WeatherKind.Snow));
            Assert.That(chances[1].Weight, Is.EqualTo(35f));
            Assert.That(chances[2].Weather, Is.EqualTo(WeatherKind.StrongWind));
            Assert.That(chances[2].Weight, Is.EqualTo(15f));
        }
    }
}

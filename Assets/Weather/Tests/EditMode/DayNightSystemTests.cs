using NUnit.Framework;

namespace SeoulZikimi.Weather.Tests
{
    public sealed class DayNightSystemTests
    {
        [Test]
        public void FixedNight_AppliesNightProfileToEveryPresenter()
        {
            var presenter = new FakePresenter();
            DayNightController controller = CreateController(presenter, 0);

            TimeOfDaySelection result = controller.Start(
                new DayNightSessionOptions(
                    true,
                    TimeOfDaySelectionMode.Fixed,
                    TimeOfDay.Night));

            Assert.That(result.TimeOfDay, Is.EqualTo(TimeOfDay.Night));
            Assert.That(presenter.LightingProfile.TimeOfDay, Is.EqualTo(TimeOfDay.Night));
            Assert.That(presenter.SkyboxProfile.SkyboxVariantKey, Is.EqualTo("Night"));
            Assert.That(presenter.SceneryProfile.SceneryVariantKey, Is.EqualTo("Night"));
        }

        [Test]
        public void RandomSelection_CanSelectNight()
        {
            var presenter = new FakePresenter();
            DayNightController controller = CreateController(presenter, 1);

            TimeOfDaySelection result = controller.Start(
                new DayNightSessionOptions(true, TimeOfDaySelectionMode.Random));

            Assert.That(result.TimeOfDay, Is.EqualTo(TimeOfDay.Night));
        }

        [Test]
        public void DisabledOption_DoesNotApplyVisualProfile()
        {
            var presenter = new FakePresenter();
            DayNightController controller = CreateController(presenter, 0);

            TimeOfDaySelection result = controller.Start(
                new DayNightSessionOptions(false, TimeOfDaySelectionMode.Random));

            Assert.That(result.IsEnabled, Is.False);
            Assert.That(controller.IsRunning, Is.False);
            Assert.That(presenter.ApplyCount, Is.Zero);
        }

        [Test]
        public void Stop_ResetsAllVisualResponsibilities()
        {
            var presenter = new FakePresenter();
            DayNightController controller = CreateController(presenter, 0);
            controller.Start(
                new DayNightSessionOptions(true, TimeOfDaySelectionMode.Fixed));

            int resetsBeforeStop = presenter.ResetCount;
            controller.Stop();

            Assert.That(presenter.ResetCount - resetsBeforeStop, Is.EqualTo(3));
            Assert.That(controller.IsRunning, Is.False);
        }

        private static DayNightController CreateController(
            FakePresenter presenter,
            int randomIndex)
        {
            return DefaultDayNightFactory.Create(
                presenter,
                presenter,
                presenter,
                new FakeRandom(randomIndex));
        }

        private sealed class FakeRandom : IRandomSource
        {
            private readonly int _index;

            public FakeRandom(int index)
            {
                _index = index;
            }

            public float NextFloat() => 0f;
            public int NextInt(int maxExclusive) => _index;
        }

        private sealed class FakePresenter :
            ITimeOfDayLightingPresenter,
            ITimeOfDaySkyboxPresenter,
            ITimeOfDaySceneryPresenter
        {
            public TimeOfDayVisualProfile LightingProfile { get; private set; }
            public TimeOfDayVisualProfile SkyboxProfile { get; private set; }
            public TimeOfDayVisualProfile SceneryProfile { get; private set; }
            public int ApplyCount { get; private set; }
            public int ResetCount { get; private set; }

            public void ApplyLighting(TimeOfDayVisualProfile profile)
            {
                LightingProfile = profile;
                ApplyCount++;
            }

            public void ResetLighting() => ResetCount++;

            public void ApplySkybox(TimeOfDayVisualProfile profile)
            {
                SkyboxProfile = profile;
                ApplyCount++;
            }

            public void ResetSkybox() => ResetCount++;

            public void ApplyScenery(TimeOfDayVisualProfile profile)
            {
                SceneryProfile = profile;
                ApplyCount++;
            }

            public void ResetScenery() => ResetCount++;
        }
    }
}

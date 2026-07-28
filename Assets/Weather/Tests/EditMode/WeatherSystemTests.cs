using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace SeoulZikimi.Weather.Tests
{
    public sealed class WeatherSystemTests
    {
        [TestCase(0.00f, WeatherKind.Sunny)]
        [TestCase(0.59f, WeatherKind.Sunny)]
        [TestCase(0.60f, WeatherKind.Rain)]
        [TestCase(0.69f, WeatherKind.Rain)]
        [TestCase(0.70f, WeatherKind.CherryBlossom)]
        [TestCase(0.99f, WeatherKind.CherryBlossom)]
        public void SpringWeather_UsesConfiguredWeights(float roll, WeatherKind expected)
        {
            var selector = new WeightedWeatherSelector(
                SeasonWeatherTable.CreateDefault(),
                new FakeRandom(roll));

            WeatherSelection result = selector.Select(
                new WeatherSessionOptions(true, SeasonSelectionMode.Fixed, Season.Spring));

            Assert.That(result.Season, Is.EqualTo(Season.Spring));
            Assert.That(result.Weather, Is.EqualTo(expected));
        }

        [Test]
        public void DisabledOption_DoesNotStartWeather()
        {
            var presenter = new FakePresenter();
            WeatherController controller = CreateController(presenter);

            WeatherSelection result = controller.Start(
                new WeatherSessionOptions(false, SeasonSelectionMode.Random));

            Assert.That(result.IsEnabled, Is.False);
            Assert.That(controller.IsRunning, Is.False);
            Assert.That(presenter.ShowCount, Is.Zero);
        }

        [Test]
        public void Rain_SlipsMovingActor_WhenRollIsWithinTenPercent()
        {
            var actor = new FakeActor { IsMoving = true };
            var effect = new SlipWeatherEffect(new FakeRandom(0.09f));

            effect.OnActorMoved(actor);

            Assert.That(actor.SlipCount, Is.EqualTo(1));
        }

        [Test]
        public void StrongWind_MovesOnlyLooseMaterials_AndDropsOneEveryFifteenSeconds()
        {
            var loose = new FakeMaterial(false);
            var fixedMaterial = new FakeMaterial(true);
            var world = new FakeWorld(loose, fixedMaterial);
            var effect = new StrongWindWeatherEffect(
                world,
                new FakeWindDirection(Vector3.right),
                new FakeRandom(0f, 0),
                moveSpeed: 2f,
                dropInterval: 15f);

            effect.Enter();
            effect.Tick(15f);

            Assert.That(loose.TotalMovement, Is.EqualTo(Vector3.right * 30f));
            Assert.That(loose.DropCount, Is.EqualTo(1));
            Assert.That(fixedMaterial.TotalMovement, Is.EqualTo(Vector3.zero));
            Assert.That(fixedMaterial.DropCount, Is.Zero);
        }

        [Test]
        public void StrongWind_DoesNothingWhenEveryMaterialIsFixed()
        {
            var fixedMaterial = new FakeMaterial(true);
            var effect = new StrongWindWeatherEffect(
                new FakeWorld(fixedMaterial),
                new FakeWindDirection(Vector3.forward),
                new FakeRandom(0f),
                dropInterval: 15f);

            Assert.DoesNotThrow(() => effect.Tick(30f));
            Assert.That(fixedMaterial.DropCount, Is.Zero);
        }

        private static WeatherController CreateController(FakePresenter presenter)
        {
            var noEffect = new NoGameplayWeatherEffect();
            var effects = new Dictionary<WeatherKind, IWeatherEffect>();
            foreach (WeatherKind weather in System.Enum.GetValues(typeof(WeatherKind)))
                effects[weather] = noEffect;

            return new WeatherController(
                new WeightedWeatherSelector(SeasonWeatherTable.CreateDefault(), new FakeRandom(0f, 0)),
                new WeatherCatalog(effects),
                presenter);
        }

        private sealed class FakeRandom : IRandomSource
        {
            private readonly float _floatValue;
            private readonly int _intValue;

            public FakeRandom(float floatValue, int intValue = 0)
            {
                _floatValue = floatValue;
                _intValue = intValue;
            }

            public float NextFloat() => _floatValue;
            public int NextInt(int maxExclusive) => _intValue;
        }

        private sealed class FakeActor : IWeatherActor
        {
            public bool IsMoving { get; set; }
            public int SlipCount { get; private set; }
            public void Slip() => SlipCount++;
        }

        private sealed class FakeMaterial : IWeatherMaterial
        {
            public bool IsFixed { get; }
            public Vector3 TotalMovement { get; private set; }
            public int DropCount { get; private set; }

            public FakeMaterial(bool isFixed)
            {
                IsFixed = isFixed;
            }

            public void Move(Vector3 displacement) => TotalMovement += displacement;
            public void Drop() => DropCount++;
        }

        private sealed class FakeWorld : IWeatherWorld
        {
            public IReadOnlyList<IWeatherMaterial> Materials { get; }

            public FakeWorld(params IWeatherMaterial[] materials)
            {
                Materials = materials;
            }
        }

        private sealed class FakeWindDirection : IWindDirectionProvider
        {
            public Vector3 CurrentDirection { get; }

            public FakeWindDirection(Vector3 currentDirection)
            {
                CurrentDirection = currentDirection;
            }
        }

        private sealed class FakePresenter : IWeatherVisualPresenter
        {
            public int ShowCount { get; private set; }
            public void Show(Season season, WeatherKind weather) => ShowCount++;
            public void Hide() { }
        }
    }
}

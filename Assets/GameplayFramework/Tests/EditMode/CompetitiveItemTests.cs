using System.Collections.Generic;
using NUnit.Framework;
using SeoulZikimi.Weather;

namespace SeoulZikimi.Gameplay.Tests
{
    public sealed class CompetitiveItemTests
    {
        [Test]
        public void DefaultCatalog_ContainsEveryItem()
        {
            var catalog = CompetitiveItemDefinitionCatalog.CreateDefault();

            Assert.That(catalog.GetAll().Count, Is.EqualTo(13));
            Assert.That(catalog.Get(CompetitiveItemKind.Cannon).Weight, Is.EqualTo(10f));
            Assert.That(catalog.Get(CompetitiveItemKind.Rain).EffectDurationSeconds, Is.EqualTo(60f));
            Assert.That(catalog.Get(CompetitiveItemKind.MovementSlow).Magnitude, Is.EqualTo(0.7f));
            Assert.That(catalog.Get(CompetitiveItemKind.MovementBoost).Magnitude, Is.EqualTo(1.3f));
        }

        [Test]
        public void TimedSpawn_CreatesOneItemEveryThirtySeconds()
        {
            var gateway = new FakeSpawnGateway();
            var director = CreateDirector(gateway);

            director.Tick(29f);
            Assert.That(gateway.Spawns.Count, Is.Zero);
            director.Tick(1f);
            Assert.That(gateway.Spawns.Count, Is.EqualTo(1));
            director.Tick(30f);
            Assert.That(gateway.Spawns.Count, Is.EqualTo(2));
        }

        [Test]
        public void CompletionReward_IsGrantedOnlyOncePerTenPercent()
        {
            var gateway = new FakeSpawnGateway();
            var director = CreateDirector(gateway);

            director.ReportCompletion("A", 25f);
            director.ReportCompletion("A", 14f);
            director.ReportCompletion("A", 20f);

            Assert.That(gateway.Spawns.Count, Is.EqualTo(2));
            Assert.That(gateway.Spawns[0].Reason, Is.EqualTo(ItemSpawnReason.CompletionMilestone));
            Assert.That(gateway.Spawns[0].BeneficiaryTeamId, Is.EqualTo("A"));
        }

        [Test]
        public void UnusedItem_ExpiresSixtySecondsAfterItsSpawnTime()
        {
            var gateway = new FakeSpawnGateway();
            var director = CreateDirector(gateway);

            director.Tick(30f);
            string firstItemId = gateway.LastSpawnedId;
            director.Tick(59f);
            Assert.That(gateway.Despawns.ContainsKey(firstItemId), Is.False);

            director.Tick(1f);
            Assert.That(gateway.Despawns[firstItemId], Is.EqualTo(ItemDespawnReason.Expired));
        }

        [Test]
        public void EnemyItem_ResolvesOpponentAndAppliesConfiguredDuration()
        {
            var definitions = CompetitiveItemDefinitionCatalog.CreateDefault();
            var target = new FakeTargets();
            CompetitiveItemEffectCatalog effects = DefaultCompetitiveItemFactory.CreateEffects(
                definitions,
                target,
                target,
                target,
                target,
                target,
                target,
                target,
                target);
            var service = new CompetitiveItemUseService(
                definitions,
                effects,
                new FakeOpponentResolver());

            CompetitiveItemUseRequest request = service.Use(
                CompetitiveItemKind.Rain,
                "player-a",
                "A");

            Assert.That(request.TargetTeamId, Is.EqualTo("B"));
            Assert.That(target.WeatherTeamId, Is.EqualTo("B"));
            Assert.That(target.WeatherKind, Is.EqualTo(WeatherKind.Rain));
            Assert.That(target.Duration, Is.EqualTo(60f));
        }

        [Test]
        public void AllyBuff_TargetsSourceTeam()
        {
            var definitions = CompetitiveItemDefinitionCatalog.CreateDefault();
            var target = new FakeTargets();
            var service = new CompetitiveItemUseService(
                definitions,
                DefaultCompetitiveItemFactory.CreateEffects(
                    definitions,
                    target,
                    target,
                    target,
                    target,
                    target,
                    target,
                    target,
                    target),
                new FakeOpponentResolver());

            service.Use(CompetitiveItemKind.MovementBoost, "player-a", "A");

            Assert.That(target.MovementTeamId, Is.EqualTo("A"));
            Assert.That(target.Multiplier, Is.EqualTo(1.3f));
            Assert.That(target.Duration, Is.EqualTo(15f));
        }

        private static CompetitiveItemSpawnDirector CreateDirector(FakeSpawnGateway gateway)
        {
            var definitions = CompetitiveItemDefinitionCatalog.CreateDefault();
            return new CompetitiveItemSpawnDirector(
                new WeightedCompetitiveItemSelector(definitions, new FakeRandom()),
                gateway,
                30f,
                10f,
                60f);
        }

        private sealed class FakeRandom : IRandomSource
        {
            public float NextFloat() => 0f;
            public int NextInt(int maxExclusive) => 0;
        }

        private sealed class FakeSpawnGateway : ICompetitiveItemSpawnGateway
        {
            private int _nextId;
            public List<CompetitiveItemSpawnRequest> Spawns { get; } = new();
            public Dictionary<string, ItemDespawnReason> Despawns { get; } = new();
            public string LastSpawnedId { get; private set; }

            public string Spawn(CompetitiveItemSpawnRequest request)
            {
                LastSpawnedId = $"item-{++_nextId}";
                Spawns.Add(request);
                return LastSpawnedId;
            }

            public void Despawn(string itemInstanceId, ItemDespawnReason reason)
                => Despawns[itemInstanceId] = reason;
        }

        private sealed class FakeOpponentResolver : IOpponentTeamResolver
        {
            public string GetOpponentTeamId(string sourceTeamId)
                => sourceTeamId == "A" ? "B" : "A";
        }

        private sealed class FakeTargets :
            IUnfixedConstructionTarget,
            ICompletedConstructionTarget,
            ITemporaryTeamWeatherTarget,
            ITeamFogTarget,
            ITeamMovementModifierTarget,
            ITeamProcessModifierTarget,
            ITeamOrderLockTarget,
            ITeamWeatherImmunityTarget
        {
            public string CannonTeamId { get; private set; }
            public void DestroyRandomCompleted(string teamId) => CannonTeamId = teamId;

            public string WeatherTeamId { get; private set; }
            public WeatherKind WeatherKind { get; private set; }
            public string MovementTeamId { get; private set; }
            public float Multiplier { get; private set; }
            public float Duration { get; private set; }

            public void CollapseAllUnfixed(string teamId) { }

            public void ApplyTemporaryWeather(string teamId, WeatherKind weather, float durationSeconds)
            {
                WeatherTeamId = teamId;
                WeatherKind = weather;
                Duration = durationSeconds;
            }

            public void ApplyFog(string teamId, float durationSeconds)
                => Duration = durationSeconds;

            public void ApplyMovementSpeedMultiplier(string teamId, float multiplier, float durationSeconds)
            {
                MovementTeamId = teamId;
                Multiplier = multiplier;
                Duration = durationSeconds;
            }

            public void ApplyProcessSpeedMultiplier(string teamId, float multiplier, float durationSeconds)
            {
                Multiplier = multiplier;
                Duration = durationSeconds;
            }

            public void LockNewOrders(string teamId, float durationSeconds)
                => Duration = durationSeconds;

            public void ApplyWeatherImmunity(string teamId, float durationSeconds)
                => Duration = durationSeconds;
        }
    }
}

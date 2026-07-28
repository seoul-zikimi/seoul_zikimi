using System;
using System.Collections.Generic;
using SeoulZikimi.Weather;

namespace SeoulZikimi.Gameplay
{
    public static class DefaultCompetitiveItemFactory
    {
        public static CompetitiveItemEffectCatalog CreateEffects(
            ICompetitiveItemDefinitionCatalog definitions,
            IUnfixedConstructionTarget construction,
            ITemporaryTeamWeatherTarget weather,
            ITeamFogTarget fog,
            ITeamMovementModifierTarget movement,
            ITeamProcessModifierTarget process,
            ITeamOrderLockTarget orders,
            ITeamWeatherImmunityTarget immunity)
        {
            if (definitions == null)
                throw new ArgumentNullException(nameof(definitions));

            var effects = new Dictionary<CompetitiveItemKind, ICompetitiveItemEffect>
            {
                [CompetitiveItemKind.Earthquake] = new EarthquakeItemEffect(
                    definitions.Get(CompetitiveItemKind.Earthquake), construction),
                [CompetitiveItemKind.Rain] = new WeatherItemEffect(
                    definitions.Get(CompetitiveItemKind.Rain), weather, WeatherKind.Rain),
                [CompetitiveItemKind.Snow] = new WeatherItemEffect(
                    definitions.Get(CompetitiveItemKind.Snow), weather, WeatherKind.Snow),
                [CompetitiveItemKind.StrongWind] = new WeatherItemEffect(
                    definitions.Get(CompetitiveItemKind.StrongWind), weather, WeatherKind.StrongWind),
                [CompetitiveItemKind.Typhoon] = new WeatherItemEffect(
                    definitions.Get(CompetitiveItemKind.Typhoon), weather, WeatherKind.Typhoon),
                [CompetitiveItemKind.Fog] = new FogItemEffect(
                    definitions.Get(CompetitiveItemKind.Fog), fog),
                [CompetitiveItemKind.MovementSlow] = new MovementModifierItemEffect(
                    definitions.Get(CompetitiveItemKind.MovementSlow), movement),
                [CompetitiveItemKind.ProcessSlow] = new ProcessModifierItemEffect(
                    definitions.Get(CompetitiveItemKind.ProcessSlow), process),
                [CompetitiveItemKind.OrderHack] = new OrderHackItemEffect(
                    definitions.Get(CompetitiveItemKind.OrderHack), orders),
                [CompetitiveItemKind.Umbrella] = new UmbrellaItemEffect(
                    definitions.Get(CompetitiveItemKind.Umbrella), immunity),
                [CompetitiveItemKind.MovementBoost] = new MovementModifierItemEffect(
                    definitions.Get(CompetitiveItemKind.MovementBoost), movement),
                [CompetitiveItemKind.ProcessBoost] = new ProcessModifierItemEffect(
                    definitions.Get(CompetitiveItemKind.ProcessBoost), process)
            };

            return new CompetitiveItemEffectCatalog(effects);
        }

        public static CompetitiveItemSpawnDirector CreateSpawnDirector(
            ICompetitiveItemSpawnGateway spawnGateway,
            IRandomSource random,
            ICompetitiveItemDefinitionCatalog definitions = null)
        {
            definitions ??= CompetitiveItemDefinitionCatalog.CreateDefault();
            var selector = new WeightedCompetitiveItemSelector(
                definitions,
                random ?? throw new ArgumentNullException(nameof(random)));

            return new CompetitiveItemSpawnDirector(
                selector,
                spawnGateway,
                worldSpawnIntervalSeconds: 30f,
                completionStepPercent: 10f,
                itemLifetimeSeconds: 60f);
        }
    }
}

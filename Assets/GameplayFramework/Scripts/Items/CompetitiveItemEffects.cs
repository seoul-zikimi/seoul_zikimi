using System;
using System.Collections.Generic;
using SeoulZikimi.Weather;

namespace SeoulZikimi.Gameplay
{
    public abstract class CompetitiveItemEffectBase : ICompetitiveItemEffect
    {
        protected CompetitiveItemDefinition Definition { get; }
        public CompetitiveItemKind Kind => Definition.Kind;

        protected CompetitiveItemEffectBase(CompetitiveItemDefinition definition)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        }

        public abstract void Apply(CompetitiveItemUseRequest request);
    }

    public sealed class EarthquakeItemEffect : CompetitiveItemEffectBase
    {
        private readonly IUnfixedConstructionTarget _target;

        public EarthquakeItemEffect(
            CompetitiveItemDefinition definition,
            IUnfixedConstructionTarget target)
            : base(definition)
        {
            _target = target ?? throw new ArgumentNullException(nameof(target));
        }

        public override void Apply(CompetitiveItemUseRequest request)
            => _target.CollapseAllUnfixed(request.TargetTeamId);
    }

    public sealed class WeatherItemEffect : CompetitiveItemEffectBase
    {
        private readonly ITemporaryTeamWeatherTarget _target;
        private readonly WeatherKind _weather;

        public WeatherItemEffect(
            CompetitiveItemDefinition definition,
            ITemporaryTeamWeatherTarget target,
            WeatherKind weather)
            : base(definition)
        {
            _target = target ?? throw new ArgumentNullException(nameof(target));
            _weather = weather;
        }

        public override void Apply(CompetitiveItemUseRequest request)
            => _target.ApplyTemporaryWeather(
                request.TargetTeamId,
                _weather,
                Definition.EffectDurationSeconds);
    }

    public sealed class FogItemEffect : CompetitiveItemEffectBase
    {
        private readonly ITeamFogTarget _target;

        public FogItemEffect(
            CompetitiveItemDefinition definition,
            ITeamFogTarget target)
            : base(definition)
        {
            _target = target ?? throw new ArgumentNullException(nameof(target));
        }

        public override void Apply(CompetitiveItemUseRequest request)
            => _target.ApplyFog(request.TargetTeamId, Definition.EffectDurationSeconds);
    }

    public sealed class MovementModifierItemEffect : CompetitiveItemEffectBase
    {
        private readonly ITeamMovementModifierTarget _target;

        public MovementModifierItemEffect(
            CompetitiveItemDefinition definition,
            ITeamMovementModifierTarget target)
            : base(definition)
        {
            _target = target ?? throw new ArgumentNullException(nameof(target));
        }

        public override void Apply(CompetitiveItemUseRequest request)
            => _target.ApplyMovementSpeedMultiplier(
                request.TargetTeamId,
                Definition.Magnitude,
                Definition.EffectDurationSeconds);
    }

    public sealed class ProcessModifierItemEffect : CompetitiveItemEffectBase
    {
        private readonly ITeamProcessModifierTarget _target;

        public ProcessModifierItemEffect(
            CompetitiveItemDefinition definition,
            ITeamProcessModifierTarget target)
            : base(definition)
        {
            _target = target ?? throw new ArgumentNullException(nameof(target));
        }

        public override void Apply(CompetitiveItemUseRequest request)
            => _target.ApplyProcessSpeedMultiplier(
                request.TargetTeamId,
                Definition.Magnitude,
                Definition.EffectDurationSeconds);
    }

    public sealed class OrderHackItemEffect : CompetitiveItemEffectBase
    {
        private readonly ITeamOrderLockTarget _target;

        public OrderHackItemEffect(
            CompetitiveItemDefinition definition,
            ITeamOrderLockTarget target)
            : base(definition)
        {
            _target = target ?? throw new ArgumentNullException(nameof(target));
        }

        public override void Apply(CompetitiveItemUseRequest request)
            => _target.LockNewOrders(request.TargetTeamId, Definition.EffectDurationSeconds);
    }

    public sealed class UmbrellaItemEffect : CompetitiveItemEffectBase
    {
        private readonly ITeamWeatherImmunityTarget _target;

        public UmbrellaItemEffect(
            CompetitiveItemDefinition definition,
            ITeamWeatherImmunityTarget target)
            : base(definition)
        {
            _target = target ?? throw new ArgumentNullException(nameof(target));
        }

        public override void Apply(CompetitiveItemUseRequest request)
            => _target.ApplyWeatherImmunity(
                request.TargetTeamId,
                Definition.EffectDurationSeconds);
    }

    public sealed class CompetitiveItemEffectCatalog : ICompetitiveItemEffectCatalog
    {
        private readonly IReadOnlyDictionary<CompetitiveItemKind, ICompetitiveItemEffect> _effects;

        public CompetitiveItemEffectCatalog(
            IReadOnlyDictionary<CompetitiveItemKind, ICompetitiveItemEffect> effects)
        {
            _effects = effects ?? throw new ArgumentNullException(nameof(effects));

            foreach (CompetitiveItemKind kind in Enum.GetValues(typeof(CompetitiveItemKind)))
            {
                if (!_effects.TryGetValue(kind, out var effect) || effect == null)
                    throw new ArgumentException($"{kind} 아이템 효과가 필요합니다.", nameof(effects));
                if (effect.Kind != kind)
                    throw new ArgumentException($"{kind} 키와 효과 구현이 일치하지 않습니다.", nameof(effects));
            }
        }

        public ICompetitiveItemEffect Get(CompetitiveItemKind kind) => _effects[kind];
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace SeoulZikimi.Weather
{
    /// <summary>효과 구현체가 필요한 메서드만 재정의할 수 있도록 기본 동작을 제공한다.</summary>
    public abstract class WeatherEffectBase : IWeatherEffect
    {
        public virtual void Enter() { }
        public virtual void Exit() { }
        public virtual void Tick(float deltaTime) { }
        public virtual void OnActorMoved(IWeatherActor actor) { }
    }

    public sealed class NoGameplayWeatherEffect : WeatherEffectBase
    {
    }

    public sealed class SlipWeatherEffect : WeatherEffectBase
    {
        private readonly IRandomSource _random;
        private readonly float _slipChance;

        public SlipWeatherEffect(IRandomSource random, float slipChance = 0.1f)
        {
            if (slipChance < 0f || slipChance > 1f)
                throw new ArgumentOutOfRangeException(nameof(slipChance));

            _random = random ?? throw new ArgumentNullException(nameof(random));
            _slipChance = slipChance;
        }

        public override void OnActorMoved(IWeatherActor actor)
        {
            if (actor == null)
                throw new ArgumentNullException(nameof(actor));

            if (actor.IsMoving && _random.NextFloat() < _slipChance)
                actor.Slip();
        }
    }

    public sealed class StrongWindWeatherEffect : WeatherEffectBase
    {
        private readonly IWeatherWorld _world;
        private readonly IWindDirectionProvider _directionProvider;
        private readonly IRandomSource _random;
        private readonly float _moveSpeed;
        private readonly float _dropInterval;
        private float _elapsedSinceDrop;

        public StrongWindWeatherEffect(
            IWeatherWorld world,
            IWindDirectionProvider directionProvider,
            IRandomSource random,
            float moveSpeed = 0.1f,
            float dropInterval = 15f)
        {
            if (moveSpeed < 0f)
                throw new ArgumentOutOfRangeException(nameof(moveSpeed));
            if (dropInterval <= 0f)
                throw new ArgumentOutOfRangeException(nameof(dropInterval));

            _world = world ?? throw new ArgumentNullException(nameof(world));
            _directionProvider = directionProvider ?? throw new ArgumentNullException(nameof(directionProvider));
            _random = random ?? throw new ArgumentNullException(nameof(random));
            _moveSpeed = moveSpeed;
            _dropInterval = dropInterval;
        }

        public override void Enter()
        {
            _elapsedSinceDrop = 0f;
        }

        public override void Tick(float deltaTime)
        {
            if (deltaTime < 0f)
                throw new ArgumentOutOfRangeException(nameof(deltaTime));

            MoveLooseMaterials(deltaTime);
            _elapsedSinceDrop += deltaTime;

            while (_elapsedSinceDrop >= _dropInterval)
            {
                _elapsedSinceDrop -= _dropInterval;
                DropOneLooseMaterial();
            }
        }

        private void MoveLooseMaterials(float deltaTime)
        {
            Vector3 direction = _directionProvider.CurrentDirection;
            Vector3 displacement = direction.sqrMagnitude > 0f
                ? direction.normalized * (_moveSpeed * deltaTime)
                : Vector3.zero;

            if (displacement == Vector3.zero)
                return;

            IReadOnlyList<IWeatherMaterial> materials = _world.Materials;
            for (int i = 0; i < materials.Count; i++)
            {
                IWeatherMaterial material = materials[i];
                if (material != null && !material.IsFixed)
                    material.Move(displacement);
            }
        }

        private void DropOneLooseMaterial()
        {
            IReadOnlyList<IWeatherMaterial> materials = _world.Materials;
            var looseMaterials = new List<IWeatherMaterial>();

            for (int i = 0; i < materials.Count; i++)
            {
                IWeatherMaterial material = materials[i];
                if (material != null && !material.IsFixed)
                    looseMaterials.Add(material);
            }

            if (looseMaterials.Count == 0)
                return;

            looseMaterials[_random.NextInt(looseMaterials.Count)].Drop();
        }
    }

    /// <summary>
    /// 태풍처럼 여러 규칙으로 이루어진 날씨를 조건문 없이 조합한다.
    /// 새 복합 날씨도 기존 효과 구현을 수정하지 않고 추가할 수 있다.
    /// </summary>
    public sealed class CompositeWeatherEffect : WeatherEffectBase
    {
        private readonly IReadOnlyList<IWeatherEffect> _effects;

        public CompositeWeatherEffect(params IWeatherEffect[] effects)
        {
            if (effects == null)
                throw new ArgumentNullException(nameof(effects));

            for (int i = 0; i < effects.Length; i++)
            {
                if (effects[i] == null)
                    throw new ArgumentException("효과 목록에 null을 넣을 수 없습니다.", nameof(effects));
            }

            _effects = effects;
        }

        public override void Enter()
        {
            for (int i = 0; i < _effects.Count; i++)
                _effects[i].Enter();
        }

        public override void Exit()
        {
            for (int i = _effects.Count - 1; i >= 0; i--)
                _effects[i].Exit();
        }

        public override void Tick(float deltaTime)
        {
            for (int i = 0; i < _effects.Count; i++)
                _effects[i].Tick(deltaTime);
        }

        public override void OnActorMoved(IWeatherActor actor)
        {
            for (int i = 0; i < _effects.Count; i++)
                _effects[i].OnActorMoved(actor);
        }
    }
}

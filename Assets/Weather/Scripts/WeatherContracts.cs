using System.Collections.Generic;
using UnityEngine;

namespace SeoulZikimi.Weather
{
    /// <summary>난수 구현을 교체할 수 있게 하여 선택 규칙과 효과를 테스트 가능하게 한다.</summary>
    public interface IRandomSource
    {
        float NextFloat();
        int NextInt(int maxExclusive);
    }

    public interface IWeatherSelector
    {
        WeatherSelection Select(WeatherSessionOptions options);
    }

    /// <summary>미끄러질 수 있는 플레이어/캐릭터가 구현할 최소 계약이다.</summary>
    public interface IWeatherActor
    {
        bool IsMoving { get; }
        void Slip();
    }

    /// <summary>강풍의 영향을 받을 수 있는 바닥 재료가 구현할 최소 계약이다.</summary>
    public interface IWeatherMaterial
    {
        bool IsFixed { get; }
        void Move(Vector3 displacement);
        void Drop();
    }

    public interface IWeatherWorld
    {
        IReadOnlyList<IWeatherMaterial> Materials { get; }
    }

    public interface IWindDirectionProvider
    {
        Vector3 CurrentDirection { get; }
    }

    /// <summary>
    /// 실제 파티클, 바닥 표현, 계절 아이콘은 추후 이 인터페이스의 구현체가 담당한다.
    /// 날씨 규칙은 구체적인 VFX나 UI를 알지 못한다.
    /// </summary>
    public interface IWeatherVisualPresenter
    {
        void Show(Season season, WeatherKind weather);
        void Hide();
    }

    public interface IWeatherEffect
    {
        void Enter();
        void Exit();
        void Tick(float deltaTime);
        void OnActorMoved(IWeatherActor actor);
    }

    public interface IWeatherCatalog
    {
        IWeatherEffect GetEffect(WeatherKind weather);
    }
}

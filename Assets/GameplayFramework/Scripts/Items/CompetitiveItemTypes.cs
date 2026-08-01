using System;

namespace SeoulZikimi.Gameplay
{
    /// <summary>
    /// 대포는 조준해서 쏘지만, 파괴 대상은 상대 진영의 완성된 파츠 중 하나를 무작위로 고른다.
    /// </summary>
    public enum CompetitiveItemKind
    {
        Earthquake,
        Rain,
        Snow,
        StrongWind,
        Typhoon,
        Fog,
        MovementSlow,
        ProcessSlow,
        OrderHack,
        Umbrella,
        MovementBoost,
        ProcessBoost,
        Cannon
    }

    public enum ItemTargetSide
    {
        Ally,
        Enemy
    }

    public enum ItemSpawnReason
    {
        TimedWorldSpawn,
        CompletionMilestone
    }

    public enum ItemDespawnReason
    {
        Consumed,
        Expired,
        RoundReset
    }

    /// <summary>아이템의 확률, 대상, 지속시간, 배율을 데이터로 보관한다.</summary>
    public sealed class CompetitiveItemDefinition
    {
        public CompetitiveItemKind Kind { get; }
        public float Weight { get; }
        public ItemTargetSide TargetSide { get; }
        public float EffectDurationSeconds { get; }
        public float Magnitude { get; }

        public CompetitiveItemDefinition(
            CompetitiveItemKind kind,
            float weight,
            ItemTargetSide targetSide,
            float effectDurationSeconds,
            float magnitude = 0f)
        {
            if (weight <= 0f)
                throw new ArgumentOutOfRangeException(nameof(weight));
            if (effectDurationSeconds < 0f)
                throw new ArgumentOutOfRangeException(nameof(effectDurationSeconds));

            Kind = kind;
            Weight = weight;
            TargetSide = targetSide;
            EffectDurationSeconds = effectDurationSeconds;
            Magnitude = magnitude;
        }
    }

    public readonly struct CompetitiveItemUseRequest
    {
        public string SourcePlayerId { get; }
        public string SourceTeamId { get; }
        public string TargetTeamId { get; }

        public CompetitiveItemUseRequest(
            string sourcePlayerId,
            string sourceTeamId,
            string targetTeamId)
        {
            if (string.IsNullOrWhiteSpace(sourcePlayerId))
                throw new ArgumentException("사용자 ID가 필요합니다.", nameof(sourcePlayerId));
            if (string.IsNullOrWhiteSpace(sourceTeamId))
                throw new ArgumentException("사용자 팀 ID가 필요합니다.", nameof(sourceTeamId));
            if (string.IsNullOrWhiteSpace(targetTeamId))
                throw new ArgumentException("대상 팀 ID가 필요합니다.", nameof(targetTeamId));

            SourcePlayerId = sourcePlayerId;
            SourceTeamId = sourceTeamId;
            TargetTeamId = targetTeamId;
        }
    }

    public readonly struct CompetitiveItemSpawnRequest
    {
        public CompetitiveItemKind Kind { get; }
        public ItemSpawnReason Reason { get; }
        public string BeneficiaryTeamId { get; }
        public float LifetimeSeconds { get; }

        public CompetitiveItemSpawnRequest(
            CompetitiveItemKind kind,
            ItemSpawnReason reason,
            string beneficiaryTeamId,
            float lifetimeSeconds)
        {
            if (lifetimeSeconds <= 0f)
                throw new ArgumentOutOfRangeException(nameof(lifetimeSeconds));

            Kind = kind;
            Reason = reason;
            BeneficiaryTeamId = beneficiaryTeamId;
            LifetimeSeconds = lifetimeSeconds;
        }
    }
}

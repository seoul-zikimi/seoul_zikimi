using System;
using System.Collections.Generic;

namespace SeoulZikimi.Gameplay
{
    /// <summary>
    /// 30초 월드 스폰, 완성도 10% 최초 달성 보상, 미사용 60초 만료를 관리한다.
    /// 실제 맵 위치 선택과 GameObject 생성은 ICompetitiveItemSpawnGateway가 담당한다.
    /// </summary>
    public sealed class CompetitiveItemSpawnDirector
    {
        private readonly ICompetitiveItemSelector _selector;
        private readonly ICompetitiveItemSpawnGateway _gateway;
        private readonly float _worldSpawnInterval;
        private readonly float _completionStepPercent;
        private readonly float _itemLifetime;
        private readonly Dictionary<string, int> _highestMilestoneByTeam = new();
        private readonly Dictionary<string, float> _remainingLifetimeByItem = new();
        private float _elapsedSinceWorldSpawn;

        public CompetitiveItemSpawnDirector(
            ICompetitiveItemSelector selector,
            ICompetitiveItemSpawnGateway gateway,
            float worldSpawnIntervalSeconds = 30f,
            float completionStepPercent = 10f,
            float itemLifetimeSeconds = 60f)
        {
            if (worldSpawnIntervalSeconds <= 0f)
                throw new ArgumentOutOfRangeException(nameof(worldSpawnIntervalSeconds));
            if (completionStepPercent <= 0f || completionStepPercent > 100f)
                throw new ArgumentOutOfRangeException(nameof(completionStepPercent));
            if (itemLifetimeSeconds <= 0f)
                throw new ArgumentOutOfRangeException(nameof(itemLifetimeSeconds));

            _selector = selector ?? throw new ArgumentNullException(nameof(selector));
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            _worldSpawnInterval = worldSpawnIntervalSeconds;
            _completionStepPercent = completionStepPercent;
            _itemLifetime = itemLifetimeSeconds;
        }

        /// <summary>서버 권위 2대2 게임 루프에서 매 프레임 호출한다.</summary>
        public void Tick(float deltaTime)
        {
            if (deltaTime < 0f)
                throw new ArgumentOutOfRangeException(nameof(deltaTime));

            float remainingDelta = deltaTime;
            while (remainingDelta > 0f)
            {
                float untilNextSpawn = _worldSpawnInterval - _elapsedSinceWorldSpawn;
                float step = Math.Min(remainingDelta, untilNextSpawn);

                TickLifetimes(step);
                _elapsedSinceWorldSpawn += step;
                remainingDelta -= step;

                if (_elapsedSinceWorldSpawn >= _worldSpawnInterval)
                {
                    _elapsedSinceWorldSpawn = 0f;
                    Spawn(ItemSpawnReason.TimedWorldSpawn, null);
                }
            }
        }

        /// <summary>
        /// 팀 완성도가 갱신될 때 호출한다.
        /// 한 번 받은 10% 단위 보상은 완성도가 내려갔다가 복구되어도 다시 지급하지 않는다.
        /// </summary>
        public void ReportCompletion(string teamId, float completionPercent)
        {
            if (string.IsNullOrWhiteSpace(teamId))
                throw new ArgumentException("팀 ID가 필요합니다.", nameof(teamId));

            float clamped = Math.Max(0f, Math.Min(100f, completionPercent));
            int reachedMilestone = (int)Math.Floor(clamped / _completionStepPercent);
            _highestMilestoneByTeam.TryGetValue(teamId, out int highestAwarded);

            if (reachedMilestone <= highestAwarded)
                return;

            for (int milestone = highestAwarded + 1; milestone <= reachedMilestone; milestone++)
                Spawn(ItemSpawnReason.CompletionMilestone, teamId);

            _highestMilestoneByTeam[teamId] = reachedMilestone;
        }

        /// <summary>아이템 사용 또는 획득이 확정됐을 때 호출해 만료 추적에서 제거한다.</summary>
        public void NotifyConsumed(string itemInstanceId)
        {
            if (string.IsNullOrWhiteSpace(itemInstanceId))
                throw new ArgumentException("아이템 인스턴스 ID가 필요합니다.", nameof(itemInstanceId));

            if (_remainingLifetimeByItem.Remove(itemInstanceId))
                _gateway.Despawn(itemInstanceId, ItemDespawnReason.Consumed);
        }

        /// <summary>새 2대2 라운드 시작 시 스폰 주기와 최초 달성 기록을 초기화한다.</summary>
        public void Reset()
        {
            var activeIds = new List<string>(_remainingLifetimeByItem.Keys);
            for (int i = 0; i < activeIds.Count; i++)
                _gateway.Despawn(activeIds[i], ItemDespawnReason.RoundReset);

            _remainingLifetimeByItem.Clear();
            _highestMilestoneByTeam.Clear();
            _elapsedSinceWorldSpawn = 0f;
        }

        private void Spawn(ItemSpawnReason reason, string beneficiaryTeamId)
        {
            var request = new CompetitiveItemSpawnRequest(
                _selector.Select(),
                reason,
                beneficiaryTeamId,
                _itemLifetime);

            string instanceId = _gateway.Spawn(request);
            if (string.IsNullOrWhiteSpace(instanceId))
                throw new InvalidOperationException("스폰 Gateway는 아이템 인스턴스 ID를 반환해야 합니다.");
            if (_remainingLifetimeByItem.ContainsKey(instanceId))
                throw new InvalidOperationException($"중복 아이템 인스턴스 ID입니다: {instanceId}");

            _remainingLifetimeByItem.Add(instanceId, _itemLifetime);
        }

        private void TickLifetimes(float deltaTime)
        {
            if (_remainingLifetimeByItem.Count == 0)
                return;

            var itemIds = new List<string>(_remainingLifetimeByItem.Keys);
            for (int i = 0; i < itemIds.Count; i++)
            {
                string itemId = itemIds[i];
                float remaining = _remainingLifetimeByItem[itemId] - deltaTime;
                if (remaining > 0f)
                {
                    _remainingLifetimeByItem[itemId] = remaining;
                    continue;
                }

                _remainingLifetimeByItem.Remove(itemId);
                _gateway.Despawn(itemId, ItemDespawnReason.Expired);
            }
        }
    }
}

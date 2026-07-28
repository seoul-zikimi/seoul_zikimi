using System;
using System.Collections.Generic;

namespace SeoulZikimi.Gameplay
{
    public interface IEarlyFinishPolicy
    {
        event Action<FinishConsentState> ConsentChanged;
        FinishConsentState ToggleConsent(string playerId);
        void RemovePlayer(string playerId);
        bool HasConsent(string playerId);
    }

    public abstract class EarlyFinishPolicyBase : IEarlyFinishPolicy
    {
        public event Action<FinishConsentState> ConsentChanged;

        public abstract FinishConsentState ToggleConsent(string playerId);
        public abstract void RemovePlayer(string playerId);
        public abstract bool HasConsent(string playerId);

        protected FinishConsentState Publish(FinishConsentState state)
        {
            ConsentChanged?.Invoke(state);
            return state;
        }
    }

    /// <summary>협동 타임어택: 현재 남아 있는 전원이 동의하면 채점으로 진행한다.</summary>
    public sealed class AllPlayersConsentPolicy : EarlyFinishPolicyBase
    {
        private readonly HashSet<string> _activePlayers;
        private readonly HashSet<string> _consents = new();

        public AllPlayersConsentPolicy(IEnumerable<string> activePlayers)
        {
            if (activePlayers == null)
                throw new ArgumentNullException(nameof(activePlayers));

            _activePlayers = new HashSet<string>(activePlayers);
        }

        public override FinishConsentState ToggleConsent(string playerId)
        {
            EnsureActive(playerId);
            if (!_consents.Add(playerId))
                _consents.Remove(playerId);

            return Publish(CreateState());
        }

        public override void RemovePlayer(string playerId)
        {
            _activePlayers.Remove(playerId);
            _consents.Remove(playerId);
            Publish(CreateState());
        }

        public override bool HasConsent(string playerId) => _consents.Contains(playerId);

        private FinishConsentState CreateState()
        {
            bool resolved = _activePlayers.Count > 0 && _consents.Count >= _activePlayers.Count;
            return new FinishConsentState(
                groupId: null,
                consentCount: _consents.Count,
                requiredCount: _activePlayers.Count,
                isResolved: resolved,
                resolution: GameEndReason.TeamConsent);
        }

        private void EnsureActive(string playerId)
        {
            if (!_activePlayers.Contains(playerId))
                throw new InvalidOperationException($"현재 게임에 없는 플레이어입니다: {playerId}");
        }
    }

    /// <summary>
    /// 2대2 대결: 항복을 요청한 플레이어의 팀원 전원이 동의하면 그 팀의 패배로 확정한다.
    /// 팀원이 이탈해 한 명만 남았다면 남은 한 명의 동의만으로 항복할 수 있다.
    /// </summary>
    public sealed class TeamSurrenderConsentPolicy : EarlyFinishPolicyBase
    {
        private readonly TeamRoster _roster;
        private readonly HashSet<string> _activePlayers;
        private readonly HashSet<string> _consents = new();

        public TeamSurrenderConsentPolicy(
            TeamRoster roster,
            IEnumerable<string> activePlayers)
        {
            _roster = roster ?? throw new ArgumentNullException(nameof(roster));
            _activePlayers = activePlayers != null
                ? new HashSet<string>(activePlayers)
                : throw new ArgumentNullException(nameof(activePlayers));
        }

        public override FinishConsentState ToggleConsent(string playerId)
        {
            EnsureActive(playerId);
            if (!_consents.Add(playerId))
                _consents.Remove(playerId);

            return Publish(CreateTeamState(_roster.GetTeamId(playerId)));
        }

        public override void RemovePlayer(string playerId)
        {
            if (!_activePlayers.Contains(playerId))
                return;

            string teamId = _roster.GetTeamId(playerId);
            _activePlayers.Remove(playerId);
            _consents.Remove(playerId);
            Publish(CreateTeamState(teamId));
        }

        public override bool HasConsent(string playerId) => _consents.Contains(playerId);

        private FinishConsentState CreateTeamState(string teamId)
        {
            int required = _roster.CountPlayersInTeam(teamId, _activePlayers);
            int consentCount = 0;

            foreach (string playerId in _consents)
            {
                if (_activePlayers.Contains(playerId)
                    && _roster.GetTeamId(playerId) == teamId)
                    consentCount++;
            }

            bool resolved = required > 0 && consentCount >= required;
            return new FinishConsentState(
                teamId,
                consentCount,
                required,
                resolved,
                GameEndReason.Surrender);
        }

        private void EnsureActive(string playerId)
        {
            if (!_activePlayers.Contains(playerId))
                throw new InvalidOperationException($"현재 게임에 없는 플레이어입니다: {playerId}");
        }
    }

    public sealed class DisabledEarlyFinishPolicy : EarlyFinishPolicyBase
    {
        public override FinishConsentState ToggleConsent(string playerId)
            => throw new InvalidOperationException("이 게임 모드는 조기 종료를 지원하지 않습니다.");

        public override void RemovePlayer(string playerId) { }
        public override bool HasConsent(string playerId) => false;
    }
}

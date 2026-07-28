using System;
using System.Collections.Generic;

namespace SeoulZikimi.Gameplay
{
    /// <summary>
    /// 플레이어와 팀의 관계만 보관한다. 실제 네트워크 팀 배정 방식과는 무관하다.
    /// </summary>
    public sealed class TeamRoster
    {
        private readonly IReadOnlyDictionary<string, string> _teamByPlayer;

        public TeamRoster(IReadOnlyDictionary<string, string> teamByPlayer)
        {
            _teamByPlayer = teamByPlayer ?? throw new ArgumentNullException(nameof(teamByPlayer));
            foreach (var pair in _teamByPlayer)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                    throw new ArgumentException("플레이어 ID가 비어 있습니다.", nameof(teamByPlayer));
                if (string.IsNullOrWhiteSpace(pair.Value))
                    throw new ArgumentException("팀 ID가 비어 있습니다.", nameof(teamByPlayer));
            }
        }

        public string GetTeamId(string playerId)
        {
            if (!_teamByPlayer.TryGetValue(playerId, out string teamId))
                throw new KeyNotFoundException($"팀이 지정되지 않은 플레이어입니다: {playerId}");

            return teamId;
        }

        public bool ContainsPlayer(string playerId) => _teamByPlayer.ContainsKey(playerId);

        public int CountPlayersInTeam(string teamId, IReadOnlyCollection<string> activePlayers)
        {
            int count = 0;
            foreach (string playerId in activePlayers)
            {
                if (_teamByPlayer.TryGetValue(playerId, out string assignedTeam)
                    && assignedTeam == teamId)
                    count++;
            }

            return count;
        }
    }

    /// <summary>
    /// TODO(Network): Unity Services/Netcode에서 4명의 플레이어를 2명씩 배정한 뒤
    /// TeamRoster를 만들어 GameplayFlowController.ConfirmTeamAssignment에 전달해야 한다.
    /// </summary>
    public interface ITeamAssignmentGateway
    {
        void RequestTeamAssignment(
            GameModeDefinition mode,
            IReadOnlyCollection<string> playerIds);
    }
}

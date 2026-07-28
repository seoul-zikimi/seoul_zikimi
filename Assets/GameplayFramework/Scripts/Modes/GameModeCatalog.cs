using System;
using System.Collections.Generic;

namespace SeoulZikimi.Gameplay
{
    public interface IGameModeCatalog
    {
        GameModeDefinition Get(GameModeKind mode);
    }

    public sealed class GameModeCatalog : IGameModeCatalog
    {
        private readonly IReadOnlyDictionary<GameModeKind, GameModeDefinition> _definitions;

        public GameModeCatalog(
            IReadOnlyDictionary<GameModeKind, GameModeDefinition> definitions)
        {
            _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));

            foreach (GameModeKind mode in Enum.GetValues(typeof(GameModeKind)))
            {
                if (!_definitions.TryGetValue(mode, out var definition) || definition == null)
                    throw new ArgumentException($"{mode} 모드 정의가 필요합니다.", nameof(definitions));
                if (definition.Kind != mode)
                    throw new ArgumentException($"{mode} 키와 모드 정의가 일치하지 않습니다.", nameof(definitions));
            }
        }

        public GameModeDefinition Get(GameModeKind mode) => _definitions[mode];

        /// <summary>
        /// 현재 기획서의 기본값이다. 수치가 바뀌면 이 팩토리 대신 외부 설정으로 같은 Catalog를 만들면 된다.
        /// </summary>
        public static GameModeCatalog CreateDefault()
        {
            return new GameModeCatalog(
                new Dictionary<GameModeKind, GameModeDefinition>
                {
                    [GameModeKind.TimeAttack] = new GameModeDefinition(
                        GameModeKind.TimeAttack,
                        minimumPlayers: 2,
                        maximumPlayers: 4,
                        teamCount: 1,
                        playersPerTeam: 4,
                        timeLimitPolicy: TimeLimitPolicy.BuildingDefinition,
                        fixedTimeLimitSeconds: 0f,
                        earlyFinishResolution: EarlyFinishResolution.FinishAndScore,
                        usesScoring: true,
                        grantsRewards: true),
                    [GameModeKind.TeamVersus] = new GameModeDefinition(
                        GameModeKind.TeamVersus,
                        minimumPlayers: 4,
                        maximumPlayers: 4,
                        teamCount: 2,
                        playersPerTeam: 2,
                        timeLimitPolicy: TimeLimitPolicy.Fixed,
                        fixedTimeLimitSeconds: 7f * 60f,
                        earlyFinishResolution: EarlyFinishResolution.Surrender,
                        usesScoring: true,
                        grantsRewards: true),
                    [GameModeKind.FreeBuild] = new GameModeDefinition(
                        GameModeKind.FreeBuild,
                        minimumPlayers: 1,
                        maximumPlayers: 4,
                        teamCount: 1,
                        playersPerTeam: 4,
                        timeLimitPolicy: TimeLimitPolicy.Unlimited,
                        fixedTimeLimitSeconds: 0f,
                        earlyFinishResolution: EarlyFinishResolution.Disabled,
                        usesScoring: false,
                        grantsRewards: false)
                });
        }
    }
}

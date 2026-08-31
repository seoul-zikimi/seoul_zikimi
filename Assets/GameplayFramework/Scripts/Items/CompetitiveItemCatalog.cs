using System;
using System.Collections.Generic;
using SeoulZikimi.Weather;

namespace SeoulZikimi.Gameplay
{
    public interface ICompetitiveItemDefinitionCatalog
    {
        CompetitiveItemDefinition Get(CompetitiveItemKind kind);
        IReadOnlyList<CompetitiveItemDefinition> GetAll();
    }

    public sealed class CompetitiveItemDefinitionCatalog : ICompetitiveItemDefinitionCatalog
    {
        private readonly IReadOnlyDictionary<CompetitiveItemKind, CompetitiveItemDefinition> _byKind;
        private readonly IReadOnlyList<CompetitiveItemDefinition> _all;

        public CompetitiveItemDefinitionCatalog(
            IReadOnlyList<CompetitiveItemDefinition> definitions)
        {
            if (definitions == null)
                throw new ArgumentNullException(nameof(definitions));

            var byKind = new Dictionary<CompetitiveItemKind, CompetitiveItemDefinition>();
            var all = new List<CompetitiveItemDefinition>(definitions.Count);
            for (int i = 0; i < definitions.Count; i++)
            {
                CompetitiveItemDefinition definition = definitions[i]
                    ?? throw new ArgumentException("아이템 정의에 null을 넣을 수 없습니다.", nameof(definitions));
                if (byKind.ContainsKey(definition.Kind))
                    throw new ArgumentException($"{definition.Kind} 정의가 중복됐습니다.", nameof(definitions));

                byKind.Add(definition.Kind, definition);
                all.Add(definition);
            }

            foreach (CompetitiveItemKind kind in Enum.GetValues(typeof(CompetitiveItemKind)))
            {
                if (!byKind.ContainsKey(kind))
                    throw new ArgumentException($"{kind} 아이템 정의가 필요합니다.", nameof(definitions));
            }

            _byKind = byKind;
            _all = all;
        }

        public CompetitiveItemDefinition Get(CompetitiveItemKind kind) => _byKind[kind];
        public IReadOnlyList<CompetitiveItemDefinition> GetAll() => _all;

        /// <summary>
        /// 기획 확률표를 그대로 가중치로 사용한다(합계 100).
        /// 합계가 100일 필요는 없으며 선택기가 자동 정규화한다.
        /// 대포는 뽑히는 빈도가 낮아 존재감이 없다는 QA 피드백으로 10 → 28로 올렸고,
        /// 그만큼 체감이 약한 날씨 계열(비/눈/강풍/태풍 5 → 4)과 안개·주문방해·아군 버프를 덜어냈다.
        /// </summary>
        public static CompetitiveItemDefinitionCatalog CreateDefault()
        {
            return new CompetitiveItemDefinitionCatalog(
                new[]
                {
                    new CompetitiveItemDefinition(
                        CompetitiveItemKind.Cannon, 28f, ItemTargetSide.Enemy, 0f),
                    new CompetitiveItemDefinition(
                        CompetitiveItemKind.Earthquake, 8f, ItemTargetSide.Enemy, 0f),
                    new CompetitiveItemDefinition(
                        CompetitiveItemKind.Rain, 4f, ItemTargetSide.Enemy, 60f),
                    new CompetitiveItemDefinition(
                        CompetitiveItemKind.Snow, 4f, ItemTargetSide.Enemy, 60f),
                    new CompetitiveItemDefinition(
                        CompetitiveItemKind.StrongWind, 4f, ItemTargetSide.Enemy, 60f),
                    new CompetitiveItemDefinition(
                        CompetitiveItemKind.Typhoon, 4f, ItemTargetSide.Enemy, 60f),
                    new CompetitiveItemDefinition(
                        CompetitiveItemKind.Fog, 8f, ItemTargetSide.Enemy, 5f),
                    new CompetitiveItemDefinition(
                        CompetitiveItemKind.MovementSlow, 8f, ItemTargetSide.Enemy, 15f, 0.7f),
                    new CompetitiveItemDefinition(
                        CompetitiveItemKind.ProcessSlow, 8f, ItemTargetSide.Enemy, 15f, 0.7f),
                    new CompetitiveItemDefinition(
                        CompetitiveItemKind.OrderHack, 6f, ItemTargetSide.Enemy, 5f),
                    new CompetitiveItemDefinition(
                        CompetitiveItemKind.Umbrella, 6f, ItemTargetSide.Ally, 30f),
                    new CompetitiveItemDefinition(
                        CompetitiveItemKind.MovementBoost, 6f, ItemTargetSide.Ally, 15f, 1.3f),
                    new CompetitiveItemDefinition(
                        CompetitiveItemKind.ProcessBoost, 6f, ItemTargetSide.Ally, 15f, 1.3f)
                });
        }
    }

    public sealed class WeightedCompetitiveItemSelector : ICompetitiveItemSelector
    {
        private readonly IReadOnlyList<CompetitiveItemDefinition> _definitions;
        private readonly IRandomSource _random;
        private readonly float _totalWeight;

        public WeightedCompetitiveItemSelector(
            ICompetitiveItemDefinitionCatalog catalog,
            IRandomSource random)
        {
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));

            _random = random ?? throw new ArgumentNullException(nameof(random));
            _definitions = catalog.GetAll();

            for (int i = 0; i < _definitions.Count; i++)
                _totalWeight += _definitions[i].Weight;
        }

        public CompetitiveItemKind Select()
        {
            float roll = _random.NextFloat() * _totalWeight;
            float cumulative = 0f;

            for (int i = 0; i < _definitions.Count; i++)
            {
                cumulative += _definitions[i].Weight;
                if (roll < cumulative)
                    return _definitions[i].Kind;
            }

            return _definitions[_definitions.Count - 1].Kind;
        }
    }
}

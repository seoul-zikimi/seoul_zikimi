using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SeoulZikimi.UI.New
{
    /// <summary>
    /// 맵 선택 드롭다운의 옵션 목록을 MapCatalog에서 만든다(방 생성 화면·로비 방 공용).
    ///
    /// 원래는 프리팹에 옵션 버튼을 고정으로 깔고 "버튼 순번 = 카탈로그 인덱스"로 썼다.
    /// 그러면 맵을 추가·삭제할 때마다 프리팹도 같이 고쳐야 하고, 안 고치면 조용히 어긋난다 —
    /// 실제로 삭제된 '남산 기믹 테스트' 버튼이 그대로 남아 있었고, 선택하면 엉뚱하게
    /// 2vs2 공터가 잡혔으며, 그 뒤 추가된 롯데월드·DDP는 버튼이 없어 아예 고를 수 없었다.
    ///
    /// 그래서 프리팹 버튼은 '풀'로만 쓰고 개수·라벨·인덱스 매핑을 전부 런타임에 카탈로그로 맞춘다.
    /// 이제 맵을 추가해도 UI는 손댈 필요가 없다.
    /// 2vs2 공터(IsVersusArena)는 대전 모드가 배경으로 자동 사용하므로 선택지에서 뺀다.
    /// </summary>
    public static class UiNewMapOptions
    {
        /// <summary>'랜덤' 항목의 로비 표시 이름.</summary>
        public const string RandomLabel = "랜덤";

        /// <summary>선택지에 올릴 인덱스를 into에 채운다. 맨 앞은 항상 '랜덤'(MapCatalog.RandomMapIndex),
        /// 그 뒤로 공터(대전 모드가 알아서 씀)와 튜토리얼(설정창의 다시보기 전용)을 뺀 카탈로그 인덱스.
        /// 카탈로그가 없으면 빈 목록.</summary>
        public static void CollectSelectable(List<int> into)
        {
            if (into == null) return;
            into.Clear();

            var catalog = GridSystem.MapCatalog.Instance;
            int count = catalog != null ? catalog.Count : 0;
            if (count == 0) return;

            into.Add(GridSystem.MapCatalog.RandomMapIndex);   // 맨 위 '랜덤'
            for (int i = 0; i < count; i++)
                if (catalog.IsSelectable(i)) into.Add(i);
        }

        /// <summary>카탈로그 인덱스의 로비 표시 이름. '랜덤' 센티널이면 "랜덤", 못 찾으면 "맵 N".</summary>
        public static string LabelOf(int catalogIndex)
        {
            if (catalogIndex == GridSystem.MapCatalog.RandomMapIndex) return RandomLabel;
            var catalog = GridSystem.MapCatalog.Instance;
            var def = catalog != null ? catalog.Get(catalogIndex) : null;
            return def != null ? def.DisplayName : $"맵 {catalogIndex + 1}";
        }

        /// <summary>
        /// 버튼 풀을 needed개로 맞춘다. 모자라면 마지막 버튼을 같은 세로 간격으로 복제하고,
        /// 남으면 숨긴다. 반환값은 실제로 쓸 버튼 배열(길이 = needed).
        /// 간격은 프리팹에 깔린 앞 두 버튼의 y 차이에서 그대로 가져온다(레이아웃 그룹이 없어 수동 배치라서).
        /// </summary>
        public static Button[] FitPool(Button[] pool, int needed)
        {
            if (needed <= 0) return Array.Empty<Button>();

            var list = new List<Button>();
            if (pool != null)
                foreach (var b in pool)
                    if (b != null) list.Add(b);

            if (list.Count == 0)
            {
                Debug.LogWarning("[UiNewMapOptions] 옵션 버튼 풀이 비었습니다 — 프리팹의 mapOptionButtons를 확인하세요.");
                return Array.Empty<Button>();
            }

            var firstRt = (RectTransform)list[0].transform;
            float step = -50f;
            if (list.Count >= 2)
            {
                float d = ((RectTransform)list[1].transform).anchoredPosition.y - firstRt.anchoredPosition.y;
                if (Mathf.Abs(d) > 0.01f) step = d;
            }
            else if (firstRt.rect.height > 1f)
            {
                step = -firstRt.rect.height;
            }

            while (list.Count < needed)
            {
                var last = list[list.Count - 1];
                var clone = UnityEngine.Object.Instantiate(last.gameObject, last.transform.parent);
                clone.name = $"Option_{list.Count + 1}";
                ((RectTransform)clone.transform).anchoredPosition =
                    ((RectTransform)last.transform).anchoredPosition + new Vector2(0f, step);

                var button = clone.GetComponent<Button>();
                if (button == null) break;          // 복제본에 Button이 없으면 더 늘려봐야 소용없다
                button.onClick.RemoveAllListeners();   // 원본에 붙어 있던 리스너까지 복제되는 것 방지
                list.Add(button);
            }

            for (int i = 0; i < list.Count; i++)
                list[i].gameObject.SetActive(i < needed);

            int take = Mathf.Min(needed, list.Count);
            return list.GetRange(0, take).ToArray();
        }

        /// <summary>옵션 버튼의 라벨 텍스트를 바꾼다(자식 Text 하나를 쓴다).</summary>
        public static void SetLabel(Button button, string text)
        {
            if (button == null) return;
            var label = button.GetComponentInChildren<Text>(true);
            if (label != null) label.text = text;
        }
    }
}

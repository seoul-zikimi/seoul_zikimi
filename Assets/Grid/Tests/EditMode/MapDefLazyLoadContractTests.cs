using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GridSystem.Tests
{
    /// <summary>
    /// 맵 카드(MapDef) 지연 로드 배선 계약.
    ///
    /// 배경·완성체 프리팹은 에디터에서 직접 참조로 돌지만, 빌드는 Resources 경로 문자열로만
    /// 지연 로드한다(로비 진입 시 전 맵이 메모리에 올라오는 iOS EXC_RESOURCE 방지 — ARCHITECTURE.md §3).
    /// 프리팹이 Resources 밖에 있거나 경로 필드가 비면 에디터에선 멀쩡한데 빌드에서만 맵이 안 뜬다.
    ///
    /// 실제 사고(2026-08-29): DDP 생성 툴이 도메인 리로드마다 배경·완성체를 Resources 밖에 다시 굽고
    /// 맵 카드를 그쪽으로 재배선 — 콘솔 에러만 남기고 조용히 회귀했다. 이 테스트가 그 회귀를 잡는다.
    /// </summary>
    public class MapDefLazyLoadContractTests
    {
        [Test]
        public void 모든_맵카드의_배경_프리팹은_Resources에서_지연_로드_가능하다()
        {
            ForEachMapDef((def, so) => AssertLazyRef(def.name, so, "m_BackgroundPrefab", "m_BackgroundPrefabPath"));
        }

        [Test]
        public void 모든_맵카드의_완성체_모델은_Resources에서_지연_로드_가능하다()
        {
            ForEachMapDef((def, so) => AssertLazyRef(def.name, so, "m_CompletedModel", "m_CompletedModelPath"));
        }

        private static void ForEachMapDef(System.Action<MapDef, SerializedObject> check)
        {
            var guids = AssetDatabase.FindAssets("t:MapDef");
            Assert.IsNotEmpty(guids, "MapDef 에셋이 하나도 없음 — 검색 필터나 프로젝트 상태 확인");
            foreach (var guid in guids)
            {
                var def = AssetDatabase.LoadAssetAtPath<MapDef>(AssetDatabase.GUIDToAssetPath(guid));
                if (def == null) continue;
                check(def, new SerializedObject(def));
            }
        }

        private static void AssertLazyRef(string owner, SerializedObject so, string refField, string pathField)
        {
            var refProp = so.FindProperty(refField);
            var pathProp = so.FindProperty(pathField);
            Assert.NotNull(refProp, $"{owner}: {refField} 직렬화 필드가 없음 — MapDef 스키마가 바뀌었으면 이 테스트도 갱신");
            Assert.NotNull(pathProp, $"{owner}: {pathField} 직렬화 필드가 없음 — MapDef 스키마가 바뀌었으면 이 테스트도 갱신");

            var asset = refProp.objectReferenceValue;
            if (asset == null) return;   // 미지정(완성체 없는 맵 등)은 계약 대상 아님 — 지연 로드도 안 일어난다

            string assetPath = AssetDatabase.GetAssetPath(asset);
            StringAssert.Contains("/Resources/", assetPath,
                $"{owner}.{refField}가 Resources 밖({assetPath})을 가리킴 — 빌드에서 로드 불가. " +
                "Assets/Resources/MapPrefabs/로 옮기고(생성 툴이면 출력 경로 상수 확인) 맵 카드를 재저장하세요.");

            Assert.IsNotEmpty(pathProp.stringValue,
                $"{owner}.{pathField}가 비어 있음 — 프리팹은 Resources 안에 있지만 경로 동기화(OnValidate)가 안 됨. " +
                "맵 카드를 인스펙터에서 한 번 저장(더티 후 Ctrl+S)하세요.");

            var loaded = Resources.Load<GameObject>(pathProp.stringValue);
            Assert.NotNull(loaded,
                $"{owner}.{pathField}='{pathProp.stringValue}' Resources.Load 실패 — 경로 필드가 낡음(에셋 이동 후 재저장 누락).");
            Assert.AreSame(asset, loaded,
                $"{owner}: 직접 참조와 Resources 경로가 서로 다른 에셋을 가리킴 — 중복 사본이 있다는 뜻. " +
                "낡은 쪽을 지우고 참조·경로를 한 사본으로 모으세요(2026-08-29 DDP 중복 사본 사고 참고).");
        }
    }
}

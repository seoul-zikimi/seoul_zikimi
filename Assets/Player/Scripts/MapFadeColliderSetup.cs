using UnityEngine;
using UnityEngine.SceneManagement;

namespace Player
{
    /// <summary>
    /// GameScene 로드 시, 콜라이더 없는 맵 메시(덤불·계단·돌 등 deco)에 시야가림 페이드용
    /// Trigger BoxCollider를 자동 부여한다. CameraObstructionFader가 콜라이더 기반이라
    /// 콜라이더 없는 메시는 못 잡던 문제를, 수동 셋업(Tools▸시야가림 페이드) 없이 해결.
    /// 트리거라 물리 충돌·지지·이동 접지·집기 레이엔 영향 없음
    /// (해당 레이캐스트는 전부 QueryTriggerInteraction.Ignore, 집기는 컴포넌트로 필터).
    /// </summary>
    public static class MapFadeColliderSetup
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            GridSystem.MapLoader.BackgroundSpawned -= Process;   // 런타임 스폰 배경도 커버(씬 스캔보다 늦게 생김)
            GridSystem.MapLoader.BackgroundSpawned += Process;
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != SceneNames.GameScene)
                return;
            Process(Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None));
        }

        /// <summary>런타임 스폰된 맵 배경 등 '나중에 생긴' 오브젝트 트리에도 페이드 콜라이더 부여(MapLoader가 호출).</summary>
        public static void Process(GameObject root)
        {
            if (root == null) return;
            Process(root.GetComponentsInChildren<MeshRenderer>(true));
        }

        private static void Process(MeshRenderer[] renderers)
        {
            int water = LayerMask.NameToLayer("Water");
            int ui = LayerMask.NameToLayer("UI");
            int ignoreRay = LayerMask.NameToLayer("Ignore Raycast");
            int transparentFx = LayerMask.NameToLayer("TransparentFX");

            int added = 0;
            foreach (var mr in renderers)
            {
                var go = mr.gameObject;
                int layer = go.layer;
                if (layer == water || layer == ui || layer == ignoreRay || layer == transparentFx)
                    continue;                                            // fader가 어차피 제외하는 레이어
                if (mr.GetComponent<Collider>() != null)
                    continue;                                            // 이미 콜라이더 있음(그리드·벽·플레이어 캡슐 등)
                if (mr.GetComponentInParent<PlayerMovement>() != null)
                    continue;                                            // 플레이어 자신 제외

                var b = mr.localBounds;                                  // 정적 배칭에도 안전한 렌더러별 로컬 AABB
                if (b.size == Vector3.zero)
                    continue;

                var box = go.AddComponent<BoxCollider>();
                box.center = b.center;                                   // 회전/스케일은 Transform이 처리
                box.size = b.size;
                box.isTrigger = true;                                    // 물리 충돌 X, fader 레이만 걸림
                added++;
            }

            if (added > 0)
                Debug.Log($"[MapFadeCollider] 시야가림용 Trigger BoxCollider {added}개 자동 추가");
        }
    }
}

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Player.EditorTools
{
    /// <summary>
    /// PlayerUnit 프리팹 루트에 OwnerNetworkAnimator를 붙이고 model의 Animator에 연결한다.
    /// 멀티플레이에서 원격 캐릭터 애니가 재생되게 하는 1회 셋업. Tools ▸ Player 메뉴로 실행.
    /// </summary>
    public static class PlayerNetworkAnimatorSetup
    {
        const string kPrefab = "Assets/Player/Prefabs/PlayerUnit.prefab";

        [MenuItem("Tools/Player/Setup Network Animator (멀티 애니 동기화)")]
        static void Setup()
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(kPrefab);
            if (asset == null) { Debug.LogError($"[NetAnim] {kPrefab} 없음"); return; }

            using (var scope = new PrefabUtility.EditPrefabContentsScope(kPrefab))
            {
                var root = scope.prefabContentsRoot;

                var anim = root.GetComponentInChildren<Animator>(includeInactive: true);
                if (anim == null)
                {
                    Debug.LogError("[NetAnim] Animator를 못 찾음 — model에 Animator + PlayerAnim.controller가 붙어 있는지 확인하세요.");
                    return;
                }

                var netAnim = root.GetComponent<OwnerNetworkAnimator>();
                if (netAnim == null) netAnim = root.AddComponent<OwnerNetworkAnimator>();
                netAnim.Animator = anim;   // 루트 NetworkAnimator → model Animator 참조

                Debug.Log($"[NetAnim] OwnerNetworkAnimator 연결 완료 (Animator='{anim.name}'). 프리팹 저장됨.");
            }

            AssetDatabase.SaveAssets();
        }
    }
}
#endif

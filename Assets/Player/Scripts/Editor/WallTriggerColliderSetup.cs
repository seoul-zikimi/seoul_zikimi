#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Player.EditorTools
{
    /// <summary>
    /// 보이는 벽처럼 콜라이더가 없는 메시에, 카메라 시야가림 페이드용 Trigger 콜라이더를 일괄 부여.
    /// Trigger라 플레이어 물리 충돌은 없고(경계는 AreaWall이 담당), CameraObstructionFader의 레이만 잡힌다.
    /// 사용법: Hierarchy에서 벽 그룹(LeftWall / MiddleWall / RightWall 등)을 선택 → 메뉴 클릭 → 씬 저장.
    /// </summary>
    public static class WallTriggerColliderSetup
    {
        [MenuItem("Tools/시야가림 페이드/선택 하위 메시에 Trigger 콜라이더 추가")]
        static void AddTriggerColliders()
        {
            if (Selection.gameObjects.Length == 0)
            {
                Debug.LogWarning("[시야가림] Hierarchy에서 벽 그룹(LeftWall 등)을 먼저 선택하세요.");
                return;
            }

            int added = 0, skipped = 0;
            foreach (var go in Selection.gameObjects)
            {
                foreach (var mr in go.GetComponentsInChildren<MeshRenderer>(includeInactive: true))
                {
                    var mf = mr.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;
                    if (mr.GetComponent<Collider>() != null) { skipped++; continue; }   // 이미 콜라이더 있으면 건너뜀

                    var box = Undo.AddComponent<BoxCollider>(mr.gameObject);
                    box.center    = mf.sharedMesh.bounds.center;   // 메시 로컬 AABB(회전/스케일은 Transform이 처리)
                    box.size      = mf.sharedMesh.bounds.size;
                    box.isTrigger = true;                          // 물리 충돌 X, 레이캐스트만 걸림
                    added++;
                }
            }

            Debug.Log($"[시야가림] Trigger BoxCollider {added}개 추가 (이미 콜라이더 있어 건너뜀 {skipped}개). 씬/프리팹 저장 잊지 마세요.");
        }
    }
}
#endif

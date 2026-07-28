using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 마이페이지 전용 씬(MyPage.unity)의 진행자.
/// 씬 = 3D 부분(옷장 배경·거울 앞 캐릭터), UI 부분은 MyPageUI 팝업을 씬 위에 띄운다.
/// 캐릭터는 씬에 놓인 프리뷰 모델(idle) — 코디 적용 시 여기 모델에 입히면 됨.
/// 씬 생성: Tools ▸ MyPage ▸ Create MyPage Scene (빌드 씬 목록 등록까지 자동).
/// </summary>
public class MyPageSceneController : MonoBehaviour
{
    [SerializeField] private GameObject m_CharacterPrefab;   // 거울 앞에 세울 캐릭터(달팽이 model.fbx)
    [SerializeField] private RuntimeAnimatorController m_IdleController;   // 있으면 idle 재생(PlayerAnim이면 기본 Idle 상태)

    private void Start()
    {
        // 캐릭터 프리뷰 스폰 — 씬에 "CharacterSpot" 빈 오브젝트가 있으면 그 위치·방향으로(직접 조정용)
        if (m_CharacterPrefab != null)
        {
            var spot = GameObject.Find("CharacterSpot");
            Vector3 pos = spot != null ? spot.transform.position : Vector3.zero;
            Quaternion rot = spot != null ? spot.transform.rotation : Quaternion.Euler(0f, 180f, 0f);
            var ch = Instantiate(m_CharacterPrefab, pos, rot);
            ch.name = "~PreviewCharacter";
            var anim = ch.GetComponentInChildren<Animator>();
            if (anim == null) anim = ch.AddComponent<Animator>();
            if (m_IdleController != null) anim.runtimeAnimatorController = m_IdleController;
            anim.applyRootMotion = false;
        }

        // UI 표시(팝업 재활용 — 닫으면 메인으로 복귀)
        if (UIManager.Instance != null)
            UIManager.Instance.ShowHUDUI<MyPageUI>();
        else
            Debug.LogWarning("[MyPage] UIManager 없음 — Bootstrap을 거치지 않고 씬을 직접 실행했나요?");
    }

    /// <summary>메인 화면으로 복귀(MyPageUI 닫기에서 호출).</summary>
    public static void ReturnToMain() => SceneManager.LoadScene(SceneNames.BootstrapScene);
}

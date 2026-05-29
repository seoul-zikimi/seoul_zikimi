using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// SoundManager 동작 확인용 데모.
/// SoundManagerDemoScene의 @SoundManagerDemo 오브젝트에 부착.
/// SoundManager 싱글톤이 같은 씬에 있어야 함.
///
/// ── BGM 페이즈 ───────────────────────────────────────────
/// [1] Lobby          [2] Building
/// [3] BuildingUrgent [4] Result
///
/// ── SFX (현재 구현된 것) ──────────────────────────────────
/// [Q] PlayerFootstep  [W] PlayerBounce
///
/// ── Stop ─────────────────────────────────────────────────
/// [F1] BGM 즉시 정지  [F2] BGM fade 정지  [F3] SFX 전체 정지
/// </summary>
public class SoundManagerDemo : MonoBehaviour
{
    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        // BGM 페이즈 전환
        if (kb.digit1Key.wasPressedThisFrame) SoundManager.Instance.SetPhase(GamePhase.Lobby);
        if (kb.digit2Key.wasPressedThisFrame) SoundManager.Instance.SetPhase(GamePhase.Building);
        if (kb.digit3Key.wasPressedThisFrame) SoundManager.Instance.SetPhase(GamePhase.BuildingUrgent);
        if (kb.digit4Key.wasPressedThisFrame) SoundManager.Instance.SetPhase(GamePhase.Result);

        // SFX — 현재 연결된 것만
        if (kb.qKey.wasPressedThisFrame) SoundManager.Instance.PlaySFX(SFXType.PlayerFootstep);
        if (kb.wKey.wasPressedThisFrame) SoundManager.Instance.PlaySFX(SFXType.PlayerBounce);

        // Stop
        if (kb.f1Key.wasPressedThisFrame) SoundManager.Instance.StopBGM();
        if (kb.f2Key.wasPressedThisFrame) SoundManager.Instance.StopBGMFade();
        if (kb.f3Key.wasPressedThisFrame) SoundManager.Instance.StopAllSFX();
    }

    void OnGUI()
    {
        GUI.Box(new Rect(10, 10, 300, 220),
            "SoundManager Demo\n\n" +
            "── BGM 페이즈 ──\n" +
            "[1] Lobby  [2] Building\n" +
            "[3] BuildingUrgent  [4] Result\n\n" +
            "── SFX ──\n" +
            "[Q] PlayerFootstep  [W] PlayerBounce\n\n" +
            "── Stop ──\n" +
            "[F1] BGM 즉시정지\n" +
            "[F2] BGM Fade 정지\n" +
            "[F3] SFX 전체정지");
    }
}

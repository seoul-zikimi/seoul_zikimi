using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// SoundManager 동작 확인용 데모.
/// SoundManagerDemoScene의 @SoundManagerDemo 오브젝트에 부착.
/// SoundManager 싱글톤이 같은 씬에 있어야 함.
///
/// ── BGM 페이즈 ───────────────────────────────────────────
/// [1] Lobby  [2] Building  [3] BuildingUrgent  [4] Result
///
/// ── 2D SFX (거리 무관) ───────────────────────────────────
/// [Q] PlayerFootstep   [W] PlayerBounce
///
/// ── 3D SFX (월드 위치 — 왼/오른쪽 패닝) ──────────────────
/// [A] 왼쪽(-8)   [D] 오른쪽(+8)   [S] 버스트(8곳 동시 → voice 풀 확인)
///
/// ── 볼륨 (믹서 라우팅 확인 — 3D도 SFX 볼륨 따라감) ───────
/// [Z]/[X] SFX -/+      [C]/[V] BGM -/+
///
/// ── 정지 ─────────────────────────────────────────────────
/// [F1] BGM 즉시  [F2] BGM fade  [F3] SFX 전체(2D+3D)
/// </summary>
public class SoundManagerDemo : MonoBehaviour
{
    // 버스트: voice 풀이 동시 발음을 각자 위치에서 내는지 확인용(서로 다른 8곳).
    static readonly Vector3[] s_BurstPoints =
    {
        new(-8, 0, 0), new(8, 0, 0), new(0, 0, 8), new(0, 0, -8),
        new(-6, 0, 6), new(6, 0, 6), new(-6, 0, -6), new(6, 0, -6),
    };

    float _sfxVol;
    float _bgmVol;

    void Start()
    {
        _sfxVol = PlayerPrefs.GetFloat("SFXVolume", 1.0f);
        _bgmVol = PlayerPrefs.GetFloat("BGMVolume", 0.8f);
    }

    void Update()
    {
        var kb = Keyboard.current;
        var sm = SoundManager.Instance;
        if (kb == null || sm == null) return;

        // BGM 페이즈 전환 (crossfade)
        if (kb.digit1Key.wasPressedThisFrame) sm.SetPhase(GamePhase.Lobby);
        if (kb.digit2Key.wasPressedThisFrame) sm.SetPhase(GamePhase.Building);
        if (kb.digit3Key.wasPressedThisFrame) sm.SetPhase(GamePhase.BuildingUrgent);
        if (kb.digit4Key.wasPressedThisFrame) sm.SetPhase(GamePhase.Result);

        // 2D SFX
        if (kb.qKey.wasPressedThisFrame) sm.PlaySFX(SFXType.PlayerFootstep);
        if (kb.wKey.wasPressedThisFrame) sm.PlaySFX(SFXType.PlayerBounce);

        // 3D SFX — 위치 기준(왼/오른쪽 패닝으로 들림)
        if (kb.aKey.wasPressedThisFrame) sm.PlaySFXAt(SFXType.PlayerBounce, new Vector3(-8, 0, 0));
        if (kb.dKey.wasPressedThisFrame) sm.PlaySFXAt(SFXType.PlayerBounce, new Vector3( 8, 0, 0));
        if (kb.sKey.wasPressedThisFrame)                 // 버스트: 8곳에서 동시 발음 → 각자 위치 유지되는지
            foreach (var p in s_BurstPoints) sm.PlaySFXAt(SFXType.PlayerBounce, p);

        // 볼륨 — 3D도 SFX 볼륨 따라가는지 확인(믹서 라우팅)
        if (kb.zKey.wasPressedThisFrame) SetSfx(_sfxVol - 0.1f);
        if (kb.xKey.wasPressedThisFrame) SetSfx(_sfxVol + 0.1f);
        if (kb.cKey.wasPressedThisFrame) SetBgm(_bgmVol - 0.1f);
        if (kb.vKey.wasPressedThisFrame) SetBgm(_bgmVol + 0.1f);

        // 정지
        if (kb.f1Key.wasPressedThisFrame) sm.StopBGM();
        if (kb.f2Key.wasPressedThisFrame) sm.StopBGMFade();
        if (kb.f3Key.wasPressedThisFrame) sm.StopAllSFX();
    }

    void SetSfx(float v) { _sfxVol = Mathf.Clamp01(v); SoundManager.Instance.SetSFXVolume(_sfxVol); }
    void SetBgm(float v) { _bgmVol = Mathf.Clamp01(v); SoundManager.Instance.SetBGMVolume(_bgmVol); }

    void OnGUI()
    {
        GUI.Box(new Rect(10, 10, 330, 300),
            "SoundManager Demo\n\n" +
            "── BGM 페이즈 ──\n" +
            "[1] Lobby  [2] Building\n" +
            "[3] BuildingUrgent  [4] Result\n\n" +
            "── 2D SFX ──\n" +
            "[Q] Footstep   [W] Bounce\n\n" +
            "── 3D SFX (위치) ──\n" +
            "[A] 왼쪽   [D] 오른쪽   [S] 버스트 8곳 동시\n\n" +
            "── 볼륨 ──\n" +
            $"[Z]/[X] SFX {_sfxVol:0.0}     [C]/[V] BGM {_bgmVol:0.0}\n\n" +
            "── 정지 ──\n" +
            "[F1] BGM 즉시  [F2] BGM Fade  [F3] SFX 전체");
    }
}

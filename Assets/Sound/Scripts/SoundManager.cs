using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// 게임 전체 BGM · SFX 담당 매니저.
/// PersistentSingleton 상속 → DontDestroyOnLoad, 중복 자동 제거.
///
/// ── BGM ──────────────────────────────────────────────────
/// SetPhase(GamePhase)  : 페이즈 전환 → 1초 crossfade
/// StopBGM()            : 즉시 정지
/// StopBGMFade()        : fade-out 후 정지
///
/// ── SFX ──────────────────────────────────────────────────
/// PlaySFX(SFXType)             : 2D 사운드 (거리 무관)
/// PlaySFXAt(SFXType, Vector3)  : 3D 사운드 (월드 위치 기준)
/// StopAllSFX()                 : 모든 SFX 즉시 정지
///
/// ── 볼륨 ─────────────────────────────────────────────────
/// SetBGMVolume(float 0~1)  : BGM 볼륨 + PlayerPrefs 저장
/// SetSFXVolume(float 0~1)  : SFX 볼륨 + PlayerPrefs 저장
/// </summary>
public class SoundManager : PersistentSingleton<SoundManager>
{
    [SerializeField] SoundLibrarySO _library;
    [SerializeField] AudioMixer     _mixer;

    [Header("SFX Pool — 동시 재생 슬롯 수. 초과 시 가장 오래된 소리를 덮어씀.")]
    [SerializeField] int   _poolSize        = 12;

    [Header("BGM crossfade 지속 시간 (초)")]
    [SerializeField] float _bgmFadeDuration = 1f;

    AudioSource   _bgmSource;
    AudioSource[] _sfxPool;
    int           _poolIndex;
    Coroutine     _fadeRoutine;

    readonly Dictionary<SFXType,   AudioClip[]> _sfxMap = new();
    readonly Dictionary<GamePhase, AudioClip>   _bgmMap = new();

    protected override void Awake()
    {
        base.Awake();                   // Singleton + DontDestroyOnLoad
        if (Instance != this) return;   // 중복 인스턴스 → Destroy 예약됨, 초기화 건너뜀

        BuildMaps();
        BuildPool();
        LoadVolumes();
    }

    // ── 초기화 ───────────────────────────────────────────

    void BuildMaps()
    {
        if (_library == null)
        {
            Debug.LogError("[SoundManager] SoundLibrary가 연결되지 않았습니다.");
            return;
        }
        foreach (var e in _library.sfxEntries) _sfxMap[e.type]  = e.clips;
        foreach (var e in _library.bgmEntries) _bgmMap[e.phase] = e.clip;
    }

    void BuildPool()
    {
        if (_mixer == null)
        {
            Debug.LogError("[SoundManager] AudioMixer가 연결되지 않았습니다.");
            return;
        }

        var sfxGroup = _mixer.FindMatchingGroups("SFX")[0];

        _sfxPool = new AudioSource[_poolSize];
        for (int i = 0; i < _poolSize; i++)
        {
            var go  = new GameObject($"SFX_{i}");
            go.transform.SetParent(transform);
            var src = go.AddComponent<AudioSource>();
            src.outputAudioMixerGroup = sfxGroup;
            _sfxPool[i] = src;
        }

        _bgmSource = gameObject.AddComponent<AudioSource>();
        _bgmSource.outputAudioMixerGroup = _mixer.FindMatchingGroups("BGM")[0];
        _bgmSource.loop = true;
    }

    void LoadVolumes()
    {
        SetBGMVolume(PlayerPrefs.GetFloat("BGMVolume", 0.8f));
        SetSFXVolume(PlayerPrefs.GetFloat("SFXVolume", 1.0f));
    }

    // ── BGM ──────────────────────────────────────────────

    /// <summary>
    /// 게임 페이즈 전환. BGM을 crossfade로 교체.
    /// 같은 Phase면 아무것도 하지 않음.
    /// </summary>
    public void SetPhase(GamePhase phase)
    {
        if (!_bgmMap.TryGetValue(phase, out var clip)) return;
        if (_bgmSource.clip == clip) return;

        if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
        _fadeRoutine = StartCoroutine(CrossfadeBGM(clip));
    }

    IEnumerator CrossfadeBGM(AudioClip next)
    {
        // fade out
        float startVol = _bgmSource.volume;
        for (float t = 0; t < _bgmFadeDuration; t += Time.deltaTime)
        {
            _bgmSource.volume = Mathf.Lerp(startVol, 0f, t / _bgmFadeDuration);
            yield return null;
        }

        _bgmSource.clip = next;
        _bgmSource.Play();

        // fade in
        for (float t = 0; t < _bgmFadeDuration; t += Time.deltaTime)
        {
            _bgmSource.volume = Mathf.Lerp(0f, 1f, t / _bgmFadeDuration);
            yield return null;
        }
        _bgmSource.volume = 1f;   // AudioSource 볼륨 = "믹서에 얼마나 보낼지" 비율 (0~1)
                                  // 실제 사용자 볼륨은 AudioMixer의 BGMVolume 파라미터가 담당
    }

    /// <summary>BGM 즉시 정지.</summary>
    public void StopBGM()
    {
        if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
        _bgmSource.Stop();
    }

    /// <summary>BGM을 fade-out 후 정지.</summary>
    public void StopBGMFade()
    {
        if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
        _fadeRoutine = StartCoroutine(FadeOutBGM());
    }

    IEnumerator FadeOutBGM()
    {
        float startVol = _bgmSource.volume;
        for (float t = 0; t < _bgmFadeDuration; t += Time.deltaTime)
        {
            _bgmSource.volume = Mathf.Lerp(startVol, 0f, t / _bgmFadeDuration);
            yield return null;
        }
        _bgmSource.Stop();
        _bgmSource.volume = 1f;   // 다음 재생을 위해 복원
    }

    // ── SFX ──────────────────────────────────────────────

    /// <summary>2D 효과음 재생 (거리 무관).</summary>
    public void PlaySFX(SFXType type)
    {
        var clip = PickClip(type);
        if (clip == null) return;

        var src = NextSrc();
        src.spatialBlend = 0f;
        src.clip         = clip;
        src.Play();
    }

    /// <summary>3D 효과음 재생 (월드 위치 기준 — 멀면 작게).</summary>
    public void PlaySFXAt(SFXType type, Vector3 worldPos)
    {
        var clip = PickClip(type);
        if (clip == null) return;

        var src = NextSrc();
        src.transform.position = worldPos;
        src.spatialBlend       = 1f;
        src.clip               = clip;
        src.Play();
    }

    /// <summary>모든 SFX 즉시 정지 (씬 전환 등).</summary>
    public void StopAllSFX()
    {
        foreach (var src in _sfxPool)
            src.Stop();
    }

    // SoundLibrary에서 랜덤 클립 선택
    AudioClip PickClip(SFXType type)
    {
        if (!_sfxMap.TryGetValue(type, out var clips) || clips.Length == 0) return null;
        return clips[Random.Range(0, clips.Length)];
    }

    // Round-robin: 슬롯을 순서대로 재사용. Pool 소진 시 가장 오래된 소리 덮어씀.
    AudioSource NextSrc()
    {
        var src = _sfxPool[_poolIndex];
        _poolIndex = (_poolIndex + 1) % _poolSize;
        return src;
    }

    // ── 볼륨 (AudioMixer logarithmic scale) ──────────────

    /// <summary>
    /// BGM 볼륨 설정 (0~1). AudioMixer의 BGMVolume 파라미터에 log 변환 적용.
    /// PlayerPrefs에 자동 저장.
    /// </summary>
    public void SetBGMVolume(float linear)
    {
        linear = Mathf.Max(linear, 0.0001f);
        _mixer.SetFloat("BGMVolume", Mathf.Log10(linear) * 20f);
        PlayerPrefs.SetFloat("BGMVolume", linear);
    }

    /// <summary>
    /// SFX 볼륨 설정 (0~1). AudioMixer의 SFXVolume 파라미터에 log 변환 적용.
    /// PlayerPrefs에 자동 저장.
    /// </summary>
    public void SetSFXVolume(float linear)
    {
        linear = Mathf.Max(linear, 0.0001f);
        _mixer.SetFloat("SFXVolume", Mathf.Log10(linear) * 20f);
        PlayerPrefs.SetFloat("SFXVolume", linear);
    }
}

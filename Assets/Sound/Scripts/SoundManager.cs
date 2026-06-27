using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using DG.Tweening;

/// <summary>
/// 게임 전체 BGM · SFX 담당 매니저.
/// Singleton&lt;SoundManager&gt; 상속 → 단일 인스턴스, DontDestroyOnLoad.
///
/// ── BGM (AudioSource 1개, phase별 loop 설정) ─────────────
/// SetPhase(GamePhase)   : 페이즈 BGM으로 DOTween crossfade
/// PlayBGM(AudioClip)    : 미등록 곡으로 crossfade
/// StopBGM() / StopBGMFade()
///
/// ── SFX ──────────────────────────────────────────────────
/// PlaySFX(SFXType) / PlaySFX(AudioClip)               : 2D (한 소스 PlayOneShot, 겹침 OK)
/// PlaySFXAt(SFXType, pos) / PlaySFXAt(AudioClip, pos) : 3D (위치별 voice 풀, round-robin)
/// StopAllSFX()                                        : 2D + 3D 전부 정지
///
/// ── 볼륨 (AudioMixer log scale, PlayerPrefs 저장) ─────────
/// SetBGMVolume(0~1) / SetSFXVolume(0~1)
///
/// SFX: 2D는 매니저 소스 1개에 PlayOneShot(겹침). 3D는 자식 소스 여러 개(_sfx3DVoices)를
///      round-robin — 소스마다 위치를 따로 둬 동시 발음이 안 섞임. 모두 SFX 믹서그룹·StopAllSFX 대상.
/// </summary>
public class SoundManager : Singleton<SoundManager>
{
    [SerializeField] SoundLibrarySO _library;
    [SerializeField] AudioMixer     _mixer;

    [Header("BGM crossfade 지속 시간 (초)")]
    [SerializeField] float _bgmFadeDuration = 1f;

    [Header("3D SFX 동시 발음 수 — 각자 위치를 유지할 수 있는 최대 동시 개수")]
    [SerializeField] int _sfx3DVoices = 8;

    // AudioSource 볼륨 = "믹서에 얼마나 보낼지" 비율(0~1). 실제 사용자 볼륨은 AudioMixer 파라미터가 담당.
    const float kBgmVolume = 1f;

    AudioSource   _bgmSource;   // BGM
    AudioSource   _sfx2D;       // 2D 효과음 — PlayOneShot으로 겹쳐 재생
    AudioSource[] _sfx3D;       // 3D 효과음 — 위치별 동시 발음 위해 자식 소스 여러 개 round-robin
    int           _sfx3DIndex;
    Tween         _bgmTween;

    readonly Dictionary<SFXType,   AudioClip[]> _sfxMap = new();
    readonly Dictionary<GamePhase, AudioClip>   _bgmMap = new();

    protected override void Awake()
    {
        base.Awake();                   // Singleton + DontDestroyOnLoad
        if (Instance != this) return;   // 중복 인스턴스 → Destroy 예약됨, 초기화 건너뜀
        DontDestroyOnLoad(this.gameObject);
        BuildMaps();
        BuildAudioSources();
        // 볼륨 적용은 Start(한 프레임 뒤)로 — AudioMixer.SetFloat는 Awake 프레임엔 안 먹는 Unity 버그.
    }

    // AudioMixer는 Awake/첫 프레임엔 SetFloat가 적용되지 않는다(Unity 버그) → 한 프레임 뒤 저장 볼륨 적용.
    // (런타임 드래그는 정상 적용되나, 시작 시 로드값이 안 먹어 재시작하면 소리가 다시 커지는 문제 해결.)
    IEnumerator Start()
    {
        yield return null;
        if (Instance == this) LoadVolumes();
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

    // BGM·2D SFX는 매니저에 직접, 3D SFX는 위치별 자식 소스 voice 풀. 모두 시작 시 1회 생성.
    void BuildAudioSources()
    {
        if (_mixer == null)
        {
            Debug.LogError("[SoundManager] AudioMixer가 연결되지 않았습니다.");
            return;
        }

        var sfxGroup = _mixer.FindMatchingGroups("SFX")[0];

        _sfx2D = gameObject.AddComponent<AudioSource>();
        _sfx2D.outputAudioMixerGroup = sfxGroup;
        _sfx2D.spatialBlend = 0f;       // 2D (거리 무관)
        _sfx2D.playOnAwake  = false;

        // 3D는 소스마다 '자기 위치(transform)'가 있어야 동시 발음이 안 섞임 → 자식 오브젝트로 voice 수만큼.
        // PlayClipAtPoint(clip, pos) 대신 풀을 쓰는 이유:
        //   ① PlayClipAtPoint는 호출마다 임시 오브젝트를 생성/파괴 → 3D가 동시 많으면 GC 부담.
        //   ② 그 임시 소스는 믹서그룹 미연결 → SFX 볼륨 슬라이더가 3D엔 안 먹음.
        //   ③ 핸들이 없어 StopAllSFX로 멈출 수 없음.
        // → 시작 시 voice 수만큼 만들어 두고(믹서그룹 연결) round-robin 재사용.
        //   트레이드오프: 동시 발음이 voice 수를 넘으면 오래된 소스를 재사용 → 그 소리는 위치 공유(필요 시 _sfx3DVoices↑).
        _sfx3D = new AudioSource[Mathf.Max(1, _sfx3DVoices)];
        for (int i = 0; i < _sfx3D.Length; i++)
        {
            var go = new GameObject($"SFX3D_{i}");
            go.transform.SetParent(transform);
            var src = go.AddComponent<AudioSource>();
            src.outputAudioMixerGroup = sfxGroup;
            src.spatialBlend = 1f;      // 3D (위치 기준)
            src.playOnAwake  = false;
            _sfx3D[i] = src;
        }

        _bgmSource = gameObject.AddComponent<AudioSource>();
        _bgmSource.outputAudioMixerGroup = _mixer.FindMatchingGroups("BGM")[0];
        _bgmSource.loop        = true;
        _bgmSource.playOnAwake = false;
    }

    void LoadVolumes()
    {
        SetBGMVolume(PlayerPrefs.GetFloat("BGMVolume", 0.8f));
        SetSFXVolume(PlayerPrefs.GetFloat("SFXVolume", 1.0f));
    }

    // ── BGM ──────────────────────────────────────────────

    /// <summary>게임 페이즈 BGM으로 crossfade. 같은 곡이면 무시.</summary>
    public void SetPhase(GamePhase phase)
    {
        if (_bgmMap.TryGetValue(phase, out var clip))
            PlayBGM(clip, ShouldLoopPhase(phase));
    }

    /// <summary>지정 클립으로 crossfade(라이브러리 미등록 곡도 가능). 같은 곡이면 무시.</summary>
    public void PlayBGM(AudioClip clip) => PlayBGM(clip, true);

    /// <summary>지정 클립으로 crossfade. loop=false면 한 번만 재생한다.</summary>
    public void PlayBGM(AudioClip clip, bool loop)
    {
        if (clip == null) return;

        if (_bgmSource.clip == clip)
        {
            _bgmSource.loop = loop;
            if (!_bgmSource.isPlaying)
                _bgmSource.Play();
            return;
        }

        _bgmTween?.Kill();
        var seq = DOTween.Sequence();
        if (_bgmSource.isPlaying)
            seq.Append(_bgmSource.DOFade(0f, _bgmFadeDuration));       // 기존 곡 fade-out
        seq.AppendCallback(() =>
            {
                _bgmSource.clip   = clip;
                _bgmSource.loop   = loop;
                _bgmSource.volume = 0f;
                _bgmSource.Play();
            })
           .Append(_bgmSource.DOFade(kBgmVolume, _bgmFadeDuration));   // 새 곡 fade-in
        _bgmTween = seq;
    }

    static bool ShouldLoopPhase(GamePhase phase)
    {
        return phase != GamePhase.Building && phase != GamePhase.BuildingUrgent;
    }

    /// <summary>BGM 즉시 정지.</summary>
    public void StopBGM()
    {
        _bgmTween?.Kill();
        _bgmSource.Stop();
    }

    /// <summary>BGM을 fade-out 후 정지.</summary>
    public void StopBGMFade()
    {
        _bgmTween?.Kill();
        _bgmTween = _bgmSource.DOFade(0f, _bgmFadeDuration)
            .OnComplete(() =>
            {
                _bgmSource.Stop();
                _bgmSource.volume = kBgmVolume;   // 다음 재생을 위해 복원
            });
    }

    // ── SFX ──────────────────────────────────────────────

    /// <summary>2D 효과음 (거리 무관).</summary>
    public void PlaySFX(SFXType type) => PlaySFX(PickClip(type));

    /// <summary>2D 효과음 — 클립 직접 지정(미등록 1회성).</summary>
    public void PlaySFX(AudioClip clip)
    {
        if (clip != null) _sfx2D.PlayOneShot(clip);   // 한 소스로 겹쳐 재생
    }

    /// <summary>3D 효과음 (월드 위치 기준 — 멀면 작게).</summary>
    public void PlaySFXAt(SFXType type, Vector3 worldPos) => PlaySFXAt(PickClip(type), worldPos);

    /// <summary>3D 효과음 — 클립 직접 지정. 다음 3D 소스를 그 위치로 옮겨 PlayOneShot(round-robin).</summary>
    public void PlaySFXAt(AudioClip clip, Vector3 worldPos)
    {
        if (clip == null) return;
        var src = _sfx3D[_sfx3DIndex];
        _sfx3DIndex = (_sfx3DIndex + 1) % _sfx3D.Length;
        src.transform.position = worldPos;
        src.PlayOneShot(clip);
    }

    /// <summary>모든 SFX 즉시 정지 (2D + 3D 전부).</summary>
    public void StopAllSFX()
    {
        _sfx2D.Stop();
        foreach (var s in _sfx3D) s.Stop();
    }

    // SoundLibrary에서 랜덤 클립 선택
    AudioClip PickClip(SFXType type)
    {
        if (!_sfxMap.TryGetValue(type, out var clips) || clips.Length == 0) return null;
        return clips[Random.Range(0, clips.Length)];
    }

    // ── 볼륨 (AudioMixer logarithmic scale) ──────────────

    /// <summary>BGM 볼륨 (0~1). 믹서 BGMVolume에 log 변환. PlayerPrefs 저장.</summary>
    public void SetBGMVolume(float linear)
    {
        linear = Mathf.Max(linear, 0.0001f);
        _mixer.SetFloat("BGMVolume", Mathf.Log10(linear) * 20f);
        PlayerPrefs.SetFloat("BGMVolume", linear);
    }

    /// <summary>SFX 볼륨 (0~1). 믹서 SFXVolume에 log 변환. PlayerPrefs 저장.</summary>
    public void SetSFXVolume(float linear)
    {
        linear = Mathf.Max(linear, 0.0001f);
        _mixer.SetFloat("SFXVolume", Mathf.Log10(linear) * 20f);
        PlayerPrefs.SetFloat("SFXVolume", linear);
    }

    /// <summary>볼륨 설정(PlayerPrefs)을 디스크에 보장 저장. UI가 설정 닫을 때 호출.
    /// SetFloat은 메모리 캐시만 갱신하므로, 드래그마다 말고 닫기/종료 시 1회 호출(I/O 폭주 방지).</summary>
    public void SaveVolumes() => PlayerPrefs.Save();

    void OnApplicationQuit() => SaveVolumes();
    void OnApplicationPause(bool paused) { if (paused) SaveVolumes(); }
}

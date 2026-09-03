using UnityEngine;
using UnityEngine.UI;

namespace GridSystem
{
    /// <summary>
    /// 2vs2 선(先)100% 완공 승리 연출 — "완공!!" 풀스크린 배너 + 슬로모(0.25x) + 승리 건물 시네마틱 줌 + 팡파레.
    /// 서버가 정산(Finish)을 연출 길이만큼 미루는 동안 전 클라가 각자 재생(GameLoopManager.EarlyVictoryRpc).
    /// 전부 코드 생성. 카메라는 별도 시네마틱 카메라(depth 90) — 메인 카메라/컨트롤러는 안 건드린다.
    /// </summary>
    public static class VictoryFx
    {
        public const float kDuration = 2.4f;   // 서버 Finish 지연은 이것 + 0.2s — 배너 걷히는 타이밍에 정산 등장

        public static void Play(int winnerTeam, int myTeam, bool byCompletion)
        {
            var go = new GameObject("~VictoryCinematic");
            var runner = go.AddComponent<VictoryCinematicRunner>();
            runner.WinnerTeam = winnerTeam;
            runner.MyTeam = myTeam;
            runner.ByCompletion = byCompletion;
        }
    }

    internal sealed class VictoryCinematicRunner : MonoBehaviour
    {
        public int WinnerTeam = -1, MyTeam = -1;   // WinnerTeam -1 = 무승부(동시 완공/동점 타임오버)
        public bool ByCompletion = true;           // true = 선100% 완공 / false = 타임오버 점수 승부

        const float kSlowScale = 0.25f;
        Camera m_Cam;
        Text m_Main;
        CanvasGroup m_Group;
        RectTransform m_MainRt;
        float m_T, m_NextSpark;
        Vector3 m_Center;
        float m_Dist;
        bool m_HasZone;

        void Start()
        {
            Time.timeScale = kSlowScale;   // OnDestroy가 반드시 1로 복원

            // 승리 팀 진영(건물) 프레이밍 — 존 정보 없으면 카메라 연출 생략(배너·슬로모만)
            int focusTeam = WinnerTeam >= 0 ? WinnerTeam : Mathf.Max(0, MyTeam);
            var zone = ItemNetwork.TeamZoneBounds(focusTeam);
            if (zone.HasValue)
            {
                var b = zone.Value;
                m_Center = new Vector3(b.center.x, b.min.y + b.size.y * 0.55f, b.center.z);
                m_Dist = Mathf.Max(8f, Mathf.Max(b.size.x, b.size.z) * 0.95f);
                m_HasZone = true;

                var camGo = new GameObject("~VictoryCam");
                camGo.transform.SetParent(transform, false);
                m_Cam = camGo.AddComponent<Camera>();
                m_Cam.depth = 90f;   // 메인 카메라 위에 통째로 그림
                m_Cam.fieldOfView = 42f;
                UpdateCam(0f);
            }

            BuildBanner();
            PlayJingle(WinnerTeam < 0 || WinnerTeam == MyTeam);
        }

        void Update()
        {
            m_T += Time.unscaledDeltaTime;
            float n = Mathf.Clamp01(m_T / VictoryFx.kDuration);

            // 슬로모 복귀 — 마지막 0.6초 동안 0.25 → 1
            float back = Mathf.InverseLerp(VictoryFx.kDuration - 0.6f, VictoryFx.kDuration, m_T);
            Time.timeScale = Mathf.Lerp(kSlowScale, 1f, back);

            if (m_HasZone)
            {
                UpdateCam(n);
                if (m_T >= m_NextSpark)   // 건물 주변 무지개 축포(슬로모라 둥실둥실 — 의도)
                {
                    m_NextSpark = m_T + 0.1f;
                    var p = m_Center + new Vector3(
                        Random.Range(-0.45f, 0.45f) * m_Dist * 0.6f,
                        Random.Range(-0.1f, 0.45f) * m_Dist * 0.6f,
                        Random.Range(-0.45f, 0.45f) * m_Dist * 0.6f);
                    var fx = ItemFx.MakeSparkPublic(p, 0.16f, Color.HSVToRGB(Random.value, 0.55f, 1f));
                    fx.vel = Vector3.up * 2.2f;
                    fx.life = 0.9f;
                    fx.spinDeg = 420f;
                    fx.spinAxis = Random.onUnitSphere;
                }
            }

            // 배너 팝인(오버슛) + 막판 페이드
            if (m_MainRt != null)
            {
                float pop = Mathf.Clamp01(m_T / 0.35f);
                float s = 1f + 2.7f * Mathf.Pow(pop - 1f, 3f) + 1.7f * Mathf.Pow(pop - 1f, 2f);
                m_MainRt.localScale = new Vector3(s, s, 1f);
                m_MainRt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(m_T * 9f) * Mathf.Exp(-m_T * 2f) * 4f);
            }
            if (m_Group != null)
                m_Group.alpha = 1f - Mathf.Clamp01((m_T - (VictoryFx.kDuration - 0.4f)) / 0.4f);

            if (m_T >= VictoryFx.kDuration) Destroy(gameObject);
        }

        void OnDestroy() => Time.timeScale = 1f;

        void UpdateCam(float n)
        {
            float e = 1f - Mathf.Pow(1f - n, 3f);   // easeOutCubic — 처음 빠르게 붙고 끝은 잔잔히
            float d = Mathf.Lerp(m_Dist * 1.5f, m_Dist * 0.9f, e);
            var dir = new Vector3(0.55f, 0.5f, -1f).normalized;
            m_Cam.transform.position = m_Center + dir * d;
            m_Cam.transform.LookAt(m_Center);
        }

        // ── 배너(오버레이 캔버스 — 시네마틱 카메라 위에도 그려짐) ──
        void BuildBanner()
        {
            var canvasGo = new GameObject("Banner", typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 480;   // ItemScreenFx(450)보다 위
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);   // 전 캔버스 공통 기준(해상도 대응 통일)
            m_Group = canvasGo.GetComponent<CanvasGroup>();

            bool tie = WinnerTeam < 0;
            bool win = tie || WinnerTeam == MyTeam;
            string main = tie ? (ByCompletion ? "동시 완공?!" : "무승부!")
                : ByCompletion ? (win ? "완공!!" : "상대팀 완공...")
                : win ? "시간 종료 — 승리!" : "시간 종료 — 패배...";
            string sub = tie ? (ByCompletion ? "총점으로 승부를 가립니다" : "완성도가 똑같다!")
                : win ? (ByCompletion ? "우리 팀 승리!" : "우리가 더 지었다!") : "다음 판엔 더 빠르게!";
            var mainColor = win ? new Color(1f, 0.82f, 0.1f) : new Color(0.75f, 0.78f, 0.85f);

            m_Main = MakeText(canvasGo.transform, main, win ? 172 : 120, mainColor, new Vector2(0f, 86f));
            m_MainRt = m_Main.rectTransform;
            m_MainRt.localScale = Vector3.zero;
            MakeText(canvasGo.transform, sub, 49, Color.white, new Vector2(0f, -43f));
        }

        static Text MakeText(Transform parent, string value, int size, Color color, Vector2 pos)
        {
            var go = new GameObject("Text", typeof(Text), typeof(Outline));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(1720f, size + 57f);
            var text = go.GetComponent<Text>();
            var font = Resources.Load<Font>("Fonts/서울한강 장체M");
            text.font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.text = value;
            var outline = go.GetComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.65f);
            outline.effectDistance = new Vector2(3f, -3f);
            return text;
        }

        // ── 팡파레(합성음 — ItemFx와 같은 방식, 에셋 불필요) ──
        static AudioClip s_Win, s_Lose;

        void PlayJingle(bool win)
        {
            var clip = win ? WinClip() : LoseClip();
            var src = gameObject.AddComponent<AudioSource>();
            src.spatialBlend = 0f;   // 2D — 어디 있든 또렷하게
            src.volume = 0.85f;
            src.clip = clip;
            src.Play();
        }

        static AudioClip Synth(string name, float dur, System.Func<float, float> wave)
        {
            const int sr = 44100;
            int n = Mathf.CeilToInt(sr * dur);
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(wave((float)i / sr), -1f, 1f);
            var clip = AudioClip.Create(name, n, 1, sr, false);
            clip.SetData(data, 0);
            return clip;
        }

        // 승리: C5-E5-G5-C6 팡파레(마지막 음 길게), 사각파 살짝 섞어 브라스 느낌
        static readonly float[] kWinNotes = { 523.25f, 659.25f, 783.99f, 1046.5f };
        static AudioClip WinClip() => s_Win != null ? s_Win : s_Win = Synth("~VictoryWin", 1.1f, t =>
        {
            float v = 0f;
            for (int i = 0; i < kWinNotes.Length; i++)
            {
                float start = i * 0.13f;
                if (t < start) continue;
                float local = t - start;
                float decay = i == kWinNotes.Length - 1 ? 3.2f : 9f;
                float sine = Mathf.Sin(2f * Mathf.PI * kWinNotes[i] * local);
                v += (sine * 0.7f + Mathf.Sign(sine) * 0.2f) * Mathf.Exp(-local * decay) * 0.45f;
            }
            return v;
        });

        // 패배: G4 → Eb4 낮게 처지는 두 음(아쉬움)
        static AudioClip LoseClip() => s_Lose != null ? s_Lose : s_Lose = Synth("~VictoryLose", 0.9f, t =>
        {
            const float kSwitch = 0.3f;
            float f = t < kSwitch ? 392f : 311.13f;
            float local = t < kSwitch ? t : t - kSwitch;
            return Mathf.Sin(2f * Mathf.PI * f * local) * Mathf.Exp(-local * 3.5f) * 0.5f;
        });
    }
}

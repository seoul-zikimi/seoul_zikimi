using SeoulZikimi.Weather;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GridSystem
{
    /// <summary>
    /// 날씨 테스트 씬 전용. 버튼/숫자키로 날씨를 바꾸고, WASD로 'Player' 태그 더미를 움직여 바닥 연출(눈 자국 등)을 확인한다.
    /// 네트워크 없이 TeamWeatherFx 로컬 연출만 돌린다.
    /// </summary>
    public sealed class WeatherTestDriver : MonoBehaviour
    {
        [SerializeField] private Transform m_Dummy;
        [SerializeField] private float m_MoveSpeed = 4f;
        [SerializeField] private bool m_Fog;

        private static readonly WeatherKind[] s_Kinds =
        {
            WeatherKind.Sunny, WeatherKind.Rain, WeatherKind.Snow, WeatherKind.StrongWind,
            WeatherKind.Typhoon, WeatherKind.AutumnLeaves, WeatherKind.CherryBlossom
        };
        private static readonly string[] s_Labels =
            { "1 화창", "2 비", "3 눈", "4 강풍", "5 태풍", "6 단풍", "7 벚꽃" };

        private WeatherKind m_Current = WeatherKind.Sunny;

        private void Start()
        {
            Apply(WeatherKind.Sunny);
            // 커맨드라인 "-weatherCapture <폴더>" 가 있으면 날씨를 순회하며 스크린샷을 찍고 종료한다(자동 확인용).
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-weatherCapture") { StartCoroutine(CaptureAll(args[i + 1])); break; }
        }

        private System.Collections.IEnumerator CaptureAll(string folder)
        {
            System.IO.Directory.CreateDirectory(folder);
            foreach (WeatherKind kind in s_Kinds)
            {
                Apply(kind);
                // 더미를 원을 그리며 움직여 눈 자국이 남게 한다
                float t = 0f;
                while (t < 3.5f)
                {
                    t += Time.deltaTime;
                    if (m_Dummy != null)
                    {
                        Vector3 next = new Vector3(Mathf.Cos(t * 1.2f) * 3f, 1f, Mathf.Sin(t * 1.2f) * 3f);
                        Vector3 dir = next - m_Dummy.position; dir.y = 0f;
                        if (dir.sqrMagnitude > 0.0001f) m_Dummy.rotation = Quaternion.LookRotation(dir, Vector3.up);
                        m_Dummy.position = next;
                    }
                    yield return null;
                }
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(folder, $"weather_{kind}.png"));
                for (int i = 0; i < 6; i++) yield return new WaitForEndOfFrame();
            }
            yield return new WaitForSeconds(1f);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(0);
#else
            Application.Quit();
#endif
        }

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) return;
            Key[] digits = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6, Key.Digit7 };
            for (int i = 0; i < s_Kinds.Length; i++)
                if (kb[digits[i]].wasPressedThisFrame) Apply(s_Kinds[i]);
            if (kb.fKey.wasPressedThisFrame) { m_Fog = !m_Fog; Apply(m_Current); }

            if (m_Dummy == null) return;
            var input = new Vector3(
                (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f), 0f,
                (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f));
            if (input.sqrMagnitude > 0f)
            {
                m_Dummy.position += input.normalized * (m_MoveSpeed * Time.deltaTime);
                m_Dummy.rotation = Quaternion.LookRotation(input, Vector3.up);
            }
        }

        private void Apply(WeatherKind kind)
        {
            m_Current = kind;
            TeamWeatherFx fx = TeamWeatherFx.Get();
            fx.SetBaseWeather(kind);
            fx.Set(WeatherKind.Sunny, m_Fog);
        }

        private void OnGUI()
        {
            const float w = 120f, h = 34f;
            GUILayout.BeginArea(new Rect(12f, 12f, w + 8f, (h + 4f) * (s_Kinds.Length + 2) + 40f), GUI.skin.box);
            GUILayout.Label($"날씨: {m_Current}");
            for (int i = 0; i < s_Kinds.Length; i++)
            {
                bool on = s_Kinds[i] == m_Current;
                GUI.color = on ? new Color(0.6f, 1f, 0.6f) : Color.white;
                if (GUILayout.Button(s_Labels[i], GUILayout.Width(w), GUILayout.Height(h))) Apply(s_Kinds[i]);
            }
            GUI.color = m_Fog ? new Color(0.6f, 1f, 0.6f) : Color.white;
            if (GUILayout.Button("F 안개", GUILayout.Width(w), GUILayout.Height(h))) { m_Fog = !m_Fog; Apply(m_Current); }
            GUI.color = Color.white;
            GUILayout.Label("WASD: 더미 이동");
            GUILayout.EndArea();
        }

        public void Configure(Transform dummy) => m_Dummy = dummy;
    }
}

using GridSystem;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;

public sealed class JobsnailGameLoopHUD : MonoBehaviour
{
    private GameLoopManager m_Loop;
    private TextMeshProUGUI m_TimerText;
    private GameObject m_TopBar;
    private bool m_UrgentBgmStarted;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != SceneNames.GameScene)
            return;

        EnsureEventSystem();
        var canvas = JobsnailUiKit.EnsureOverlayCanvas("@JobsnailGameLoopHUD", 120);
        var hud = canvas.GetComponent<JobsnailGameLoopHUD>();
        if (hud == null)
            canvas.gameObject.AddComponent<JobsnailGameLoopHUD>();
        else
            hud.Rebuild();
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null)
            return;

        var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        DontDestroyOnLoad(es);
    }

    private void Awake()
    {
        Rebuild();
    }

    private void Update()
    {
        if (m_Loop == null)
            m_Loop = FindFirstObjectByType<GameLoopManager>();

        bool ready = m_Loop != null && m_Loop.IsSpawned;
        SetVisible(ready);
        if (!ready)
            return;

        if (m_Loop.IsBuilding)
        {
            if (m_Loop.TimeLeft <= 0f)
            {
                m_Loop.RequestFinishByTimeout();
                return;
            }

            if (m_Loop.TimeLeft > 60f)
                m_UrgentBgmStarted = false;

            if (!m_UrgentBgmStarted && m_Loop.TimeLeft <= 60f)
            {
                m_UrgentBgmStarted = true;
                if (SoundManager.Instance != null)
                    SoundManager.Instance.SetPhase(global::GamePhase.BuildingUrgent);
            }
        }

        int secs = Mathf.CeilToInt(m_Loop.TimeLeft);
        m_TimerText.text = m_Loop.IsBuilding ? $"{secs / 60}:{secs % 60:00}" : "종료";
    }

    private void Rebuild()
    {
        var root = transform;

        for (int i = root.childCount - 1; i >= 0; i--)
            Destroy(root.GetChild(i).gameObject);

        m_Loop = null;
        m_UrgentBgmStarted = false;

        var top = JobsnailUiKit.Box("TopBar", root, new Vector2(0.42f, 0.92f), new Vector2(0.58f, 0.99f), Vector2.zero, Vector2.zero, new Color(0.84f, 0.82f, 0.70f, 0.92f));
        m_TopBar = top.gameObject;
        m_TimerText = JobsnailUiKit.Label("Timer", top.transform, "0:00", 34, Color.black, TextAlignmentOptions.Center, Vector2.zero, Vector2.zero);

        if (SoundManager.Instance != null)
            SoundManager.Instance.SetPhase(global::GamePhase.Building);
    }

    private void SetVisible(bool visible)
    {
        if (m_TopBar != null)
            m_TopBar.SetActive(visible);
    }
}

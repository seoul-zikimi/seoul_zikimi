using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 튜토리얼 진행 관리자. TutorialSession.IsActive 상태로 GameScene에 진입하면 자동 생성되어
/// TutorialQuestData(Resources/Tutorial/TutorialQuestData)를 순서대로 재생한다.
/// 스텝 하나 = 대사(TutorialTextBoxHUD로 한 줄씩 표시) + 완료 조건(플레이어 조작을 실제로 감시).
/// 조건 감시는 Player/Grid 쪽에 추가된 로컬 전용 이벤트(LocalXxx)를 구독하거나, 누적치를 매 프레임 잰다.
/// </summary>
public class TutorialManager : MonoBehaviour
{
    private TutorialQuestData m_Data;
    private int m_StepIndex;
    private bool m_WaitingCondition;

    // 누적형 조건(이동 누적초 / 카메라·정답 회전각) 상태
    private float m_ConditionAccum;
    private int m_ConditionCount;
    private bool m_HasPrevCamYaw;
    private float m_PrevCamYaw;
    private bool m_HasAnswerStartYaw;
    private float m_AnswerStartYaw;

    private Player.PlayerInputHandler m_InputHandler;
    private Player.PlayerCameraController m_CamCtrl;
    private GridSystem.AnswerPreview m_AnswerPreview;
    private GridSystem.GameLoopManager m_Loop;

    private TutorialQuestStep CurrentStep =>
        (m_Data != null && m_Data.Steps != null && m_StepIndex >= 0 && m_StepIndex < m_Data.Steps.Count)
            ? m_Data.Steps[m_StepIndex] : null;

    // ── 부트스트랩: GameScene 로드 + 튜토리얼 세션이면 자동 생성 ──
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != SceneNames.GameScene || !TutorialSession.IsActive) return;
        if (FindFirstObjectByType<TutorialManager>() != null) return;   // 중복 생성 방지
        var go = new GameObject("~TutorialManager");
        go.AddComponent<TutorialManager>();
    }

    private void OnEnable()
    {
        Player.PlayerCarry.LocalMaterialPickedUp += OnMaterialPickedUp;
        Player.PlayerCarry.LocalToolPickedUp += OnToolPickedUp;
        Player.PlayerCarry.LocalMaterialPlaced += OnMaterialPlaced;
        Player.PlayerCarry.LocalMaterialProcessed += OnMaterialProcessed;
        Player.PlayerUnit.LocalScaffoldFloorReached += OnScaffoldFloorReached;
        GridSystem.MaterialDepot.LocalOrderRequested += OnMaterialOrdered;
    }

    private void OnDisable()
    {
        Player.PlayerCarry.LocalMaterialPickedUp -= OnMaterialPickedUp;
        Player.PlayerCarry.LocalToolPickedUp -= OnToolPickedUp;
        Player.PlayerCarry.LocalMaterialPlaced -= OnMaterialPlaced;
        Player.PlayerCarry.LocalMaterialProcessed -= OnMaterialProcessed;
        Player.PlayerUnit.LocalScaffoldFloorReached -= OnScaffoldFloorReached;
        GridSystem.MaterialDepot.LocalOrderRequested -= OnMaterialOrdered;
    }

    private void Start()
    {
        m_Data = Resources.Load<TutorialQuestData>("Tutorial/TutorialQuestData");
        if (m_Data == null || m_Data.Steps == null || m_Data.Steps.Count == 0)
        {
            Debug.LogWarning("[TutorialManager] TutorialQuestData가 없습니다 — " +
                "Jobsnail ▸ Tutorial ▸ Generate Default Tutorial Quest Data 실행 후 벽/지붕 MaterialId를 채워주세요.");
            return;
        }
        BeginStep(0);
    }

    private void Update()
    {
        if (!m_WaitingCondition) return;
        var step = CurrentStep;
        if (step == null) return;

        switch (step.Condition)
        {
            case TutorialConditionType.MoveAccumulate:
                EnsureInputHandler();
                if (m_InputHandler != null && m_InputHandler.MoveInput.sqrMagnitude > 0.01f)
                    m_ConditionAccum += Time.deltaTime;
                if (m_ConditionAccum >= step.TargetValue) CompleteCondition();
                break;

            case TutorialConditionType.CameraRotateAngle:
                EnsureCameraController();
                if (m_CamCtrl != null && m_CamCtrl.CameraArm != null)
                {
                    float y = m_CamCtrl.CameraArm.eulerAngles.y;
                    if (m_HasPrevCamYaw) m_ConditionAccum += Mathf.Abs(Mathf.DeltaAngle(m_PrevCamYaw, y));
                    m_PrevCamYaw = y; m_HasPrevCamYaw = true;
                }
                if (m_ConditionAccum >= step.TargetValue) CompleteCondition();
                break;

            case TutorialConditionType.AnswerRotateAngle:
                EnsureAnswerPreview();
                if (m_AnswerPreview != null)
                {
                    if (!m_HasAnswerStartYaw) { m_AnswerStartYaw = m_AnswerPreview.PreviewYaw; m_HasAnswerStartYaw = true; }
                    m_ConditionAccum = Mathf.Abs(m_AnswerPreview.PreviewYaw - m_AnswerStartYaw);
                }
                if (m_ConditionAccum >= step.TargetValue) CompleteCondition();
                break;
        }
    }

    private void EnsureInputHandler() { if (m_InputHandler == null) m_InputHandler = FindFirstObjectByType<Player.PlayerInputHandler>(); }
    private void EnsureCameraController() { if (m_CamCtrl == null) m_CamCtrl = FindFirstObjectByType<Player.PlayerCameraController>(); }
    private void EnsureAnswerPreview() { if (m_AnswerPreview == null) m_AnswerPreview = FindFirstObjectByType<GridSystem.AnswerPreview>(); }

    // ── 이벤트형 조건 ──
    private void OnMaterialOrdered(int materialId) => TryEventCondition(TutorialConditionType.MaterialOrdered, materialId);
    private void OnMaterialPickedUp(int materialId) => TryEventCondition(TutorialConditionType.MaterialPickedUp, materialId);
    private void OnToolPickedUp(GridSystem.ProcessType tool) => TryEventCondition(TutorialConditionType.ToolPickedUp, -1, tool);
    private void OnMaterialPlaced(int materialId, Vector3Int cell) => TryCountCondition(TutorialConditionType.MaterialPlaced, materialId);
    private void OnMaterialProcessed(GridSystem.ProcessType tool, Vector3Int cell) => TryCountCondition(TutorialConditionType.MaterialProcessed, -1, tool);

    private void OnScaffoldFloorReached(int floor)
    {
        if (!m_WaitingCondition) return;
        var step = CurrentStep;
        if (step == null || step.Condition != TutorialConditionType.ScaffoldFloorReached) return;
        if (floor >= step.TargetValue) CompleteCondition();
    }

    private void TryEventCondition(TutorialConditionType type, int materialId, GridSystem.ProcessType tool = default)
    {
        if (!m_WaitingCondition) return;
        var step = CurrentStep;
        if (step == null || step.Condition != type) return;
        if (type == TutorialConditionType.ToolPickedUp) { if (tool != step.ToolProcess) return; }
        else if (step.MaterialId >= 0 && step.MaterialId != materialId) return;
        CompleteCondition();
    }

    private void TryCountCondition(TutorialConditionType type, int materialId, GridSystem.ProcessType tool = default)
    {
        if (!m_WaitingCondition) return;
        var step = CurrentStep;
        if (step == null || step.Condition != type) return;
        if (type == TutorialConditionType.MaterialProcessed) { if (tool != step.ToolProcess) return; }
        else if (step.MaterialId >= 0 && step.MaterialId != materialId) return;
        m_ConditionCount++;
        if (m_ConditionCount >= Mathf.RoundToInt(step.TargetValue)) CompleteCondition();
    }

    // ── 스텝 진행 ──
    private void BeginStep(int index)
    {
        m_StepIndex = index;
        m_WaitingCondition = false;
        m_ConditionAccum = 0f;
        m_ConditionCount = 0;
        m_HasPrevCamYaw = false;
        m_HasAnswerStartYaw = false;

        var step = CurrentStep;
        if (step == null) { OnTutorialFinished(); return; }

        if (UIManager.Instance == null) return;
        var box = UIManager.Instance.ShowHUDUI<TutorialTextBoxHUD>();
        box.ShowLines(step.Lines, OnLinesFinished);
    }

    private void OnLinesFinished()
    {
        var step = CurrentStep;
        if (step == null) return;
        if (step.Condition == TutorialConditionType.None) { BeginStep(m_StepIndex + 1); return; }
        m_WaitingCondition = true;   // 이후 Update()/이벤트 핸들러가 조건을 감시
    }

    private void CompleteCondition()
    {
        if (!m_WaitingCondition) return;
        m_WaitingCondition = false;
        BeginStep(m_StepIndex + 1);
    }

    private void OnTutorialFinished()
    {
        SaveService.TutorialCompleted = true;
        TutorialSession.IsActive = false;
        if (m_Loop == null) m_Loop = FindFirstObjectByType<GridSystem.GameLoopManager>();
        if (m_Loop != null) m_Loop.RequestLeaveToLobby();
    }
}

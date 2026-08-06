using System.Collections;
using System.Collections.Generic;
using GridSystem;
using Player;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// 튜토리얼 인트로 → 11개 퀘스트 → 아웃트로 진행 상태 머신.
/// GameScene 로드 시 TutorialFlowController가 남긴 1회성 플래그를 소비했을 때만 활성화되므로,
/// 일반 게임에는 전혀 관여하지 않는다. 완료 판정은 전부 기존 스크립트의 공개 상태를 읽기만 한다
/// (PlayerCarry/GridNetwork/AnswerPanelFocus/GridContract 등) — 이벤트 추가나 수정 없음.
/// </summary>
public class TutorialQuestSequence : MonoBehaviour
{
    // main 병합 후 실제 튜토리얼 정답(Assets/Grid/Data/Ans_Tutorial.asset, Map_Tutorial이 참조)이 쓰는 재료명.
    // 앞쪽 벽은 문이 뚫린 별도 재료(entrance)라 왼쪽/오른쪽 벽과 재료가 다르다.
    private const string kWallMaterialName = "벽";
    private const string kDoorWallMaterialName = "문이 있는 벽";
    private const string kRoofMaterialName = "지붕";
    private const float kRotateThreshold = 720f;   // 카메라/정답 회전 누적 판정(느슨한 목측 기준 — 필요시 조정)

    // 벽/지붕이 3×2×1 이상의 다칸 오브젝트라 칸 하나가 아니라 차지하는 칸 전부를 검사해야 한다.
    // Ans_Tutorial.asset의 실제 배치 좌표와 반드시 같은 값으로 유지해야 한다(정답 데이터 갱신 시 같이 수정).
    private static List<Vector3Int> Box(int x0, int x1, int y0, int y1, int z0, int z1)
    {
        var cells = new List<Vector3Int>();
        for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
                for (int z = z0; z <= z1; z++)
                    cells.Add(new Vector3Int(x, y, z));
        return cells;
    }

    private static List<Vector3Int> LeftWallCells()  => Box(0, 0, 0, 1, 1, 3);   // 왼쪽 벽 — x=0, z 1~3
    private static List<Vector3Int> RightWallCells() => Box(3, 3, 0, 1, 1, 3);   // 오른쪽 벽 — x=3, z 1~3
    private static List<Vector3Int> FrontWallCells() => Box(0, 3, 0, 1, 0, 0);   // 앞쪽(문) 벽 — z=0, x 0~3
    private static List<Vector3Int> RoofCells()      => Box(0, 3, 2, 3, 0, 3);   // 지붕 — y 2~3 전체

    private static readonly string[] kIntroLines =
    {
        "반갑습니다. 당신은 건축업을 하는 민달팽이입니다. 당신의 목표는 열심히 일해 달팽이집을 마련하는 것입니다.",
        "건축은 혼자 진행할 수도 있지만, 다른 민달팽이들과 협동한다면 더욱 수월할 것입니다.",
    };

    private static readonly string[] kOutroLines =
    {
        "튜토리얼을 마쳤습니다. 이후 튜토리얼을 다시 진행할 수 있고, 게임 내에서 툴팁 UI를 통해 조작키를 확인할 수 있습니다.",
        "등껍질을 장만하는 그날까지 열심히 건축합시다!",
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != SceneNames.GameScene)
            return;
        if (!TutorialFlowController.ConsumeTutorialFlag())
            return;

        Debug.Log("[TutorialQuestSequence] 튜토리얼 플래그 확인됨 — 진행 상태 머신 시작");
        new GameObject("~TutorialQuestSequence").AddComponent<TutorialQuestSequence>();
    }

    private List<TutorialQuestStep> m_Steps;
    private int m_Index = -1;   // -1 = 인트로, -2 = 아웃트로/종료, 0..N-1 = 퀘스트
    private bool m_Active;

    private GameLoopManager m_Loop;
    private GridManager m_Grid;
    private GridNetwork m_Net;
    private PlayerCarry m_LocalCarry;
    private PlayerInputHandler m_LocalInput;

    private int m_WallMaterialId = MaterialCatalog.NoMaterial;
    private int m_DoorWallMaterialId = MaterialCatalog.NoMaterial;
    private int m_RoofMaterialId = MaterialCatalog.NoMaterial;
    private List<Vector3Int> m_LeftCells = new();
    private List<Vector3Int> m_RightCells = new();
    private List<Vector3Int> m_FrontCells = new();
    private List<Vector3Int> m_RoofCells = new();

    private float m_MoveHeldTime;
    private float m_CamRotAccum;
    private float m_AnswerRotAccum;
    private bool m_ReachedFloor3;

    private void Start()
    {
        StartCoroutine(RunWhenReady());
    }

    private IEnumerator RunWhenReady()
    {
        float timeout = Time.unscaledTime + 10f;
        while (Time.unscaledTime < timeout)
        {
            if (m_Loop == null) m_Loop = FindFirstObjectByType<GameLoopManager>();
            if (m_Grid == null) m_Grid = FindFirstObjectByType<GridManager>();
            if (m_Net == null) m_Net = FindFirstObjectByType<GridNetwork>();
            FindLocalPlayer();

            if (m_Loop != null && m_Loop.IsSpawned && m_Grid != null && m_Net != null && m_LocalCarry != null)
                break;
            yield return null;
        }

        if (m_Loop == null || m_Grid == null || m_Net == null || m_LocalCarry == null)
        {
            Debug.LogWarning("[TutorialQuestSequence] 필요한 컴포넌트를 찾지 못해 튜토리얼 진행을 시작하지 못했습니다.");
            Destroy(gameObject);
            yield break;
        }

        // GameLoopManager.OnNetworkSpawn()이 이미 자유 모드(시간제한 없음)를 반영했으니, 다음 일반 게임에
        // 영향 주지 않도록 로비 모드 선택값을 원래대로 되돌려 놓는다(TutorialFlowController 참고).
        TutorialFlowController.RestorePreTutorialMode();

        // GameLoopManager.OnNetworkSpawn()이 정답 선택(SelectAnswer)까지 끝냈는지 여유를 두고 확인.
        yield return null;
        yield return null;

        ResolveMaterialIds();
        m_LeftCells = LeftWallCells();
        m_RightCells = RightWallCells();
        m_FrontCells = FrontWallCells();
        m_RoofCells = RoofCells();
        m_Steps = BuildSteps();

        m_Active = true;
        var dlg = UIManager.Instance.ShowHUDUI<TutorialDialogueHUD>();
        dlg.OnSkipRequested -= OnSkipRequested;
        dlg.OnSkipRequested += OnSkipRequested;

        dlg.ShowLines(kIntroLines, () => EnterStep(0));
    }

    private void FindLocalPlayer()
    {
        if (m_LocalCarry != null) return;
        foreach (var carry in FindObjectsByType<PlayerCarry>(FindObjectsSortMode.None))
        {
            if (!carry.IsOwner) continue;
            m_LocalCarry = carry;
            m_LocalInput = carry.GetComponent<PlayerInputHandler>();
            break;
        }
    }

    private void ResolveMaterialIds()
    {
        var materials = m_Grid.Catalog != null ? m_Grid.Catalog.Materials : null;
        if (materials == null) return;
        foreach (var def in materials)
        {
            if (def == null) continue;
            if (def.name == kWallMaterialName) m_WallMaterialId = def.Id;
            else if (def.name == kDoorWallMaterialName) m_DoorWallMaterialId = def.Id;
            else if (def.name == kRoofMaterialName) m_RoofMaterialId = def.Id;
        }

        if (m_WallMaterialId == MaterialCatalog.NoMaterial || m_DoorWallMaterialId == MaterialCatalog.NoMaterial || m_RoofMaterialId == MaterialCatalog.NoMaterial)
            Debug.LogWarning($"[TutorialQuestSequence] MaterialCatalog에서 '{kWallMaterialName}'/'{kDoorWallMaterialName}'/'{kRoofMaterialName}'을(를) 찾지 못했습니다 — MaterialCatalog.asset 등록을 확인하세요.");
    }

    private bool CellsPlaced(List<Vector3Int> cells, int materialId)
    {
        if (cells == null || cells.Count == 0) return false;
        foreach (var cell in cells)
        {
            if (!m_Net.TryGetCell(cell, out int matId, out _) || matId != materialId)
                return false;
        }
        return true;
    }

    private bool CellsFixed(List<Vector3Int> cells, int materialId)
    {
        if (cells == null || cells.Count == 0) return false;
        foreach (var cell in cells)
        {
            if (!m_Net.TryGetCell(cell, out int matId, out int mask) || matId != materialId)
                return false;
            if ((mask & (int)ProcessType.Fixed) == 0)
                return false;
        }
        return true;
    }

    private bool AnyWallPickupExists()
    {
        foreach (var body in FindObjectsByType<GridSystem.PickupBody>(FindObjectsSortMode.None))
            if (body.MaterialId == m_WallMaterialId)
                return true;
        return false;
    }

    private static bool AnyMoveKeyHeld()
    {
        var kb = Keyboard.current;
        if (kb == null) return false;
        return kb.wKey.isPressed || kb.aKey.isPressed || kb.sKey.isPressed || kb.dKey.isPressed;
    }

    private List<TutorialQuestStep> BuildSteps()
    {
        var steps = new List<TutorialQuestStep>
        {
            new(new[]
            {
                "우선, w / a / s / d 를 눌러 움직여 볼까요?",
                "shift 키를 누르며 이동하면 달릴 수 있고, space 키를 누르면 점프합니다.",
            }, () =>
            {
                if (AnyMoveKeyHeld()) m_MoveHeldTime += Time.deltaTime;
                return m_MoveHeldTime >= 4f;
            }, () => m_MoveHeldTime = 0f),

            new(new[]
            {
                "마우스 우클릭을 누른 채 화면을 드래그하면, 카메라를 돌릴 수 있습니다.",
                "스크롤을 통해 카메라를 확대/축소할 수 있습니다. 주변을 둘러보세요!",
            }, () =>
            {
                if (m_LocalInput != null && !AnswerPanelFocus.Active)
                    m_CamRotAccum += m_LocalInput.CameraRotate.magnitude;
                return m_CamRotAccum >= kRotateThreshold;
            }, () => m_CamRotAccum = 0f),

            new(new[]
            {
                "좌측 하단엔, 오늘 지어야 하는 건물의 완성된 모습이 표시됩니다.",
                "정답 UI에 마우스를 대고 카메라와 동일하게 조작하며 둘러볼 수 있습니다. 주변을 둘러보세요!",
            }, () =>
            {
                if (m_LocalInput != null && AnswerPanelFocus.Active)
                    m_AnswerRotAccum += m_LocalInput.CameraRotate.magnitude;
                return m_AnswerRotAccum >= kRotateThreshold;
            }, () => m_AnswerRotAccum = 0f),

            new(new[]
            {
                "건축에 필요한 재료들은 우측 드로어 UI에서 주문할 수 있습니다.",
                "드로어 UI는 화살표 버튼을 눌러 접거나 열 수 있습니다. '벽' 재료를 주문해보세요!",
            }, AnyWallPickupExists),

            new(new[]
            {
                "주문한 재료는 주문 배송지에 도착합니다.",
                "도착한 왼쪽 벽을 클릭해 들어봅시다!",
            }, () => m_LocalCarry.IsHolding),

            new(new[]
            {
                "이제 벽을 건축할 곳으로 운반해 배치해봅시다.",
                "투명 답안의 맞는 위치에 클릭해 배치하세요! 우선 왼쪽 벽부터 배치해봅시다.",
                "오브젝트를 든 채로 R버튼을 누르면 회전시킬 수 있습니다.",
            }, () => CellsPlaced(m_LeftCells, m_WallMaterialId)),

            new(new[]
            {
                "답안은 Tab키를 눌러 보이거나 보이지 않게 할 수 있습니다.",
                "배치한 왼쪽 벽 위에 망치 아이콘이 보이시나요? 해당 아이콘은 이 오브젝트가 '고정' 되어야함을 나타냅니다.",
                "망치 도구를 클릭해 들어보세요.",
            }, () => m_LocalCarry.IsHoldingTool),

            new(new[]
            {
                "망치를 든 채로, 왼쪽 벽에 E키를 꾹 눌러 망치질을 하면 고정됩니다.",
                "이런 식으로, 공정이 필요한 오브젝트들이 있습니다. 두 개의 공정이 필요한 경우도 있고, 필요하지 않은 경우도 있습니다.",
                "공정을 잘못 진행했을 경우, z키를 꾹 누르면 공정 취소가 가능합니다.",
            }, () => CellsFixed(m_LeftCells, m_WallMaterialId)),

            new(new[]
            {
                "어떤 맵은 이미 약간의 건축이 되어 있거나, 일부 재료들이 맵 곳곳에 존재하는 경우가 있습니다.",
                "이제 오른쪽 벽과 앞쪽 벽을 알맞게 배치하고 고정해 보세요.",
            }, () => CellsFixed(m_RightCells, m_WallMaterialId) && CellsFixed(m_FrontCells, m_DoorWallMaterialId)),

            new(new[]
            {
                "마지막으로, 이제 지붕이 남았습니다.",
                "민달팽이는 원하는 벽 앞에서 w키를 누르면 벽을 기어오를 수 있습니다. 하지만 벽을 기어올라가 건축하기에 공간이 부족할 때가 있습니다.",
                "그럴 때를 대비해 '비계' 오브젝트를 제공합니다. 비계 오브젝트는 무제한으로 제공되며, 스페이스바를 2번 연타하면 자동으로 바닥에 깔립니다.",
                "비계 깔기를 통해 3층까지 올라가보세요!",
            }, () =>
            {
                if (GridContract.LocalBuildFloor >= 2) m_ReachedFloor3 = true;
                return m_ReachedFloor3;
            }, () => m_ReachedFloor3 = false),

            new(new[]
            {
                "지붕을 들고, 비계 깔기를 통해 한 층 올라가 지붕을 설치해보세요!",
            }, () => CellsPlaced(m_RoofCells, m_RoofMaterialId)),
        };
        return steps;
    }

    private void Update()
    {
        if (!m_Active) return;
        if (m_Index < 0 || m_Index >= m_Steps.Count) return;
        if (m_Steps[m_Index].IsComplete())
            EnterStep(m_Index + 1);
    }

    // 퀘스트 전환이 너무 매끄럽게 느껴지지 않도록, 이전 퀘스트 완료 표시 줄 + 현재 진행도([퀘스트 N/11])를
    // 대사 맨 앞에 붙여 넣는다 — 플레이어가 "완료됐다"는 걸 명확히 보고 한 번 더 클릭해야 다음으로 넘어간다.
    private void EnterStep(int index)
    {
        m_Index = index;
        if (index >= m_Steps.Count)
        {
            ShowOutro();
            return;
        }
        var step = m_Steps[index];
        step.OnEnter?.Invoke();

        var displayLines = new List<string>();
        if (index > 0)
            displayLines.Add($"✅ 퀘스트 {index} 완료!");
        for (int i = 0; i < step.Lines.Length; i++)
            displayLines.Add(i == 0 ? $"[퀘스트 {index + 1}/{m_Steps.Count}] {step.Lines[i]}" : step.Lines[i]);

        UIManager.Instance.ShowHUDUI<TutorialDialogueHUD>().ShowLines(displayLines, null);
    }

    private void ShowOutro()
    {
        m_Index = -2;
        var displayLines = new List<string> { $"✅ 퀘스트 {m_Steps.Count} 완료!" };
        displayLines.AddRange(kOutroLines);
        UIManager.Instance.ShowHUDUI<TutorialDialogueHUD>().ShowLines(displayLines, FinishTutorial);
    }

    private void OnSkipRequested() => FinishTutorial();

    private void FinishTutorial()
    {
        if (!m_Active) return;
        m_Active = false;
        UIManager.Instance.HideHUDUI<TutorialDialogueHUD>();
        if (m_Loop != null) m_Loop.RequestLeaveToLobby();
        Destroy(gameObject);
    }
}

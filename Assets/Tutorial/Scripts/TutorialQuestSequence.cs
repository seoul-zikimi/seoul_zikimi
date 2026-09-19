using System.Collections;
using System.Collections.Generic;
using GridSystem;
using Player;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 튜토리얼 인트로 → 12개 퀘스트 → 아웃트로 진행 상태 머신.
/// GameScene 로드 시 TutorialFlowController가 남긴 1회성 플래그를 소비했을 때만 활성화되므로,
/// 일반 게임에는 전혀 관여하지 않는다. 완료 판정은 전부 기존 스크립트의 공개 상태/이벤트를 읽기만 한다
/// (PlayerCarry/GridNetwork/AnswerPanelFocus/GridContract 등) — 게임플레이 코드 수정 없음.
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

    private static readonly LocCache<string[]> s_kIntroLines = new();
    private static string[] kIntroLines => s_kIntroLines.Get(() => new string[]{
        L.T("반갑습니다, 건축 레인저!\n무너진 서울의 명소를 다시 지어 봅시다.", "Welcome, Build Ranger!\nLet's rebuild Seoul's fallen landmarks."),
    });

    // 퀘스트별 완료 조건(BuildSteps의 순서·판정과 반드시 같이 고칠 것) — 각 퀘스트 마지막 줄 밑에 '목표 : …'로 붙는다.
    private static readonly LocCache<string[]> s_kGoalsPc = new();
    private static string[] kGoalsPc => s_kGoalsPc.Get(() => new string[]{
        L.T("W A S D 로 4초 동안 움직이기", "Move with W A S D for 4 seconds"),
        L.T("카메라를 크게 돌려보기", "Swing the camera around"),
        L.T("휴대폰의 계획도 위에서 우클릭 드래그로 돌려보기", "Right-drag on the phone's blueprint to spin it"),
        L.T("휴대폰에서 '벽' 카드 클릭 → [주문!] 누르기", "Click the 'Wall' card on the phone → press [Order!]"),
        L.T("도착한 '벽'에 다가가 클릭해서 들기", "Walk up to the delivered 'Wall' and click to pick it up"),
        L.T("벽을 든 채 G 키로 던지기", "Throw the wall with G"),
        L.T("벽을 다시 들고, 반투명한 '왼쪽 벽' 자리에 클릭해서 놓기", "Pick the wall up again and click the see-through 'left wall' spot to place it"),
        L.T("도구함의 망치를 클릭해서 들기", "Click the hammer in the toolbox to pick it up"),
        L.T("왼쪽 벽을 망치로 고정하기", "Fix the left wall with the hammer"),
        L.T("오른쪽 벽('벽')과 앞쪽 벽('문이 있는 벽')을 주문해 놓고, 둘 다 고정하기", "Order and place the right wall ('Wall') and front wall ('Wall with Door'), then fix both"),
        L.T("비계를 깔며 3층 높이까지 올라가기", "Lay scaffolding and climb up to the 3rd floor"),
        L.T("'지붕'을 주문해 들고, 비계로 올라가 지붕 자리에 놓기", "Order the 'Roof', climb up with scaffolding, and place it on the roof spot"),
    });

    private static readonly LocCache<string[]> s_kGoalsMobile = new();
    private static string[] kGoalsMobile => s_kGoalsMobile.Get(() => new string[]{
        L.T("조이스틱으로 4초 동안 움직이기", "Move with the joystick for 4 seconds"),
        L.T("카메라를 크게 돌려보기", "Swing the camera around"),
        L.T("휴대폰 버튼 누르기", "Press the Phone button"),
        L.T("휴대폰에서 '벽' 카드 터치 → [주문!] 누르기", "Tap the 'Wall' card on the phone → press [Order!]"),
        L.T("도착한 '벽'에 다가가 터치해서 들기", "Walk up to the delivered 'Wall' and tap to pick it up"),
        L.T("벽을 든 채 던지기 버튼으로 던지기", "Throw the wall with the Throw button"),
        L.T("벽을 다시 들고, 반투명한 '왼쪽 벽' 자리를 터치해서 놓기", "Pick the wall up again and tap the see-through 'left wall' spot to place it"),
        L.T("도구함의 망치를 터치해서 들기", "Tap the hammer in the toolbox to pick it up"),
        L.T("왼쪽 벽을 망치로 고정하기", "Fix the left wall with the hammer"),
        L.T("오른쪽 벽('벽')과 앞쪽 벽('문이 있는 벽')을 주문해 놓고, 둘 다 고정하기", "Order and place the right wall ('Wall') and front wall ('Wall with Door'), then fix both"),
        L.T("비계를 깔며 3층 높이까지 올라가기", "Lay scaffolding and climb up to the 3rd floor"),
        L.T("'지붕'을 주문해 들고, 비계로 올라가 지붕 자리에 놓기", "Order the 'Roof', climb up with scaffolding, and place it on the roof spot"),
    });

    private static readonly LocCache<string[]> s_kOutroLines = new();
    private static string[] kOutroLines => s_kOutroLines.Get(() => new string[]{
        L.T("튜토리얼을 마쳤습니다!\n재료를 정확히 놓고 공정을 모두 끝낼수록 완성도와 보수가 올라갑니다.", "Tutorial complete!\nThe more accurately you place and finish every process, the higher your grade and pay."),
    });

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
    private bool m_ThrewHeldObject;
    private bool m_ReachedFloor3;
    private bool m_PhoneToggled, m_LastPanelOpen;   // 모바일 퀘스트3: 휴대폰 여닫기 감지

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

        m_LocalCarry.OnThrow += OnLocalThrow;

        ResolveMaterialIds();
        m_LeftCells = LeftWallCells();
        m_RightCells = RightWallCells();
        m_FrontCells = FrontWallCells();
        m_RoofCells = RoofCells();
        m_Steps = BuildSteps();

        // 로딩 화면이 걷힌 뒤에 첫 대화를 띄운다 — 가려진 채로 뜨면 대화창의 '처음 2초 넘김 잠금'이 로딩 뒤에서 다 지나가 버린다.
        while (m_Loop != null && !m_Loop.MatchStarted)
            yield return null;

        // 좌상단 조작법 패널이 대화창과 겹친다 — 튜토리얼 동안만 접어 둔다(PlayerPrefs엔 안 남겨 다음 판엔 유저 설정대로).
        // 모바일은 조작법 패널 자체를 띄우지 않는다(GameLoopHUD).
        if (!MobileControlsHUD.ShouldUseMobileUI)
            UIManager.Instance.ShowHUDUI<ControlsTooltipHUD>().SetCollapsed(true, playSfx: false, remember: false);
        // 모바일 상단의 제스처 안내("빈 화면 드래그 · 카메라 …")도 대화창 자리와 겹친다 — 튜토리얼 동안 숨기고 OnDestroy에서 되돌린다.
        MobileControlsHUD.SetGestureHintVisible(false);

        m_Active = true;
        var dlg = UIManager.Instance.ShowHUDUI<TutorialDialogueHUD>();
        dlg.OnSkipRequested -= OnSkipRequested;
        dlg.OnSkipRequested += OnSkipRequested;

        dlg.ShowLines(kIntroLines, () => EnterStep(0));
    }

    private void OnLocalThrow() => m_ThrewHeldObject = true;

    private void OnDestroy()
    {
        MobileControlsHUD.SetGestureHintVisible(true);   // 컨트롤 캔버스는 씬을 넘어 살아 있다 — 다음 판을 위해 복구
        if (m_LocalCarry != null) m_LocalCarry.OnThrow -= OnLocalThrow;
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

    // 이동 입력 중인가 — 키보드를 직접 읽으면 모바일 조이스틱·패드에서 영영 안 깨진다(입력 핸들러 경유).
    private bool AnyMoveInput() => m_LocalInput != null && m_LocalInput.MoveInput.sqrMagnitude > 0.01f;

    // 모바일은 키보드·마우스가 없다 — 같은 퀘스트를 터치 조작 말로 안내한다.
    private static string[] Lines(string[] pc, string[] mobile) => MobileControlsHUD.ShouldUseMobileUI ? mobile : pc;

    private List<TutorialQuestStep> BuildSteps()
    {
        var steps = new List<TutorialQuestStep>
        {
            new(Lines(new[]
            {
                L.T("W A S D 로 이동, Shift 로 달리기, Space 로 점프합니다.", "Move with W A S D, hold Shift to run, and press Space to jump."),
            }, new[]
            {
                L.T("조이스틱으로 이동합니다.\n끝까지 밀면 달리고, 점프 버튼으로 점프합니다.", "Move with the joystick.\nPush it all the way to run, and press Jump to jump."),
            }), () =>
            {
                if (AnyMoveInput()) m_MoveHeldTime += Time.deltaTime;
                return m_MoveHeldTime >= 4f;
            }, () => m_MoveHeldTime = 0f),

            new(Lines(new[]
            {
                L.T("우클릭을 누른 채 드래그하면 카메라가 돌고,\n마우스 휠로 확대/축소합니다.", "Hold right mouse button and drag to rotate the camera,\nand scroll the wheel to zoom."),
            }, new[]
            {
                L.T("빈 화면을 드래그하면 카메라가 돌고,\n두 손가락으로 확대/축소합니다.", "Drag empty screen space to rotate the camera,\nand pinch with two fingers to zoom."),
            }), () =>
            {
                if (m_LocalInput != null && !AnswerPanelFocus.Active)
                    m_CamRotAccum += m_LocalInput.CameraRotate.magnitude;
                return m_CamRotAccum >= kRotateThreshold;
            }, () => m_CamRotAccum = 0f),

            new(Lines(new[]
            {
                L.T("우측 하단 휴대폰에 오늘 지을 건물의 계획도가 있습니다.", "The phone at the bottom right shows the blueprint of today's building."),
            }, new[]
            {
                L.T("휴대폰 버튼을 누르면 오늘 지을 건물의 계획도가 열립니다.", "Press the Phone button to open the blueprint of today's building."),
            }), () =>
            {
                // 모바일은 계획도 회전 조작이 없다(마우스 전용) — 휴대폰을 한 번 여닫으면 완료.
                if (MobileControlsHUD.ShouldUseMobileUI)
                {
                    if (AnswerPreview.PanelOpen != m_LastPanelOpen) m_PhoneToggled = true;
                    m_LastPanelOpen = AnswerPreview.PanelOpen;
                    return m_PhoneToggled;
                }
                if (m_LocalInput != null && AnswerPanelFocus.Active)
                    m_AnswerRotAccum += m_LocalInput.CameraRotate.magnitude;
                return m_AnswerRotAccum >= kRotateThreshold;
            }, () => { m_AnswerRotAccum = 0f; m_PhoneToggled = false; m_LastPanelOpen = AnswerPreview.PanelOpen; }),

            new(Lines(new[]
            {
                L.T("재료는 휴대폰에서 주문합니다.", "Order materials from the phone."),
            }, new[]
            {
                L.T("재료는 휴대폰에서 주문합니다.", "Order materials from the phone."),
            }), AnyWallPickupExists),

            new(Lines(new[]
            {
                L.T("주문한 재료는 배송지에 도착합니다.", "Ordered materials arrive at the delivery point."),
            }, new[]
            {
                L.T("주문한 재료는 배송지에 도착합니다.", "Ordered materials arrive at the delivery point."),
            }), () => m_LocalCarry.IsHolding),

            new(Lines(new[]
            {
                L.T("G 키로 든 물건을 던집니다. 오래 누를수록 멀리 날아갑니다.", "Press G to throw what you're holding. Hold longer to throw farther."),
            }, new[]
            {
                L.T("던지기 버튼으로 든 물건을 던집니다. 오래 누를수록 멀리 날아갑니다.", "Press the Throw button to throw what you're holding. Hold longer to throw farther."),
            }), () => m_ThrewHeldObject, () => m_ThrewHeldObject = false),

            new(Lines(new[]
            {
                L.T("반투명하게 보이는 건물이 정답 위치입니다.\n든 재료는 R 키로 돌릴 수 있습니다.", "The see-through building shows where things go.\nPress R to rotate what you're holding."),
            }, new[]
            {
                L.T("반투명하게 보이는 건물이 정답 위치입니다.\n든 재료는 회전 버튼으로 돌릴 수 있습니다.", "The see-through building shows where things go.\nUse the Rotate button to rotate what you're holding."),
            }), () => CellsPlaced(m_LeftCells, m_WallMaterialId)),

            new(Lines(new[]
            {
                L.T("벽 위의 망치 아이콘은 '고정'이 필요하다는 뜻입니다.\n고정하기 전의 벽은 부딪히면 무너지니 조심하세요!", "The hammer icon above the wall means it needs to be 'fixed'.\nUntil then, bumping into it knocks it down — careful!"),
            }, new[]
            {
                L.T("벽 위의 망치 아이콘은 '고정'이 필요하다는 뜻입니다.\n고정하기 전의 벽은 부딪히면 무너지니 조심하세요!", "The hammer icon above the wall means it needs to be 'fixed'.\nUntil then, bumping into it knocks it down — careful!"),
            }), () => m_LocalCarry.IsHoldingTool),

            new(Lines(new[]
            {
                L.T("망치를 든 채 벽에 대고 E 키를 꾹 누르면 고정됩니다.\n실수했다면 Z 키를 꾹 눌러 취소할 수 있습니다.", "With the hammer, aim at the wall and hold E to fix it.\nMade a mistake? Hold Z to undo."),
            }, new[]
            {
                L.T("망치를 든 채 벽 가까이에서 공정 버튼을 꾹 누르면 고정됩니다.\n실수했다면 공정취소 버튼을 꾹 눌러 취소할 수 있습니다.", "With the hammer, stand near the wall and hold the Process button to fix it.\nMade a mistake? Hold the Undo button."),
            }), () => CellsFixed(m_LeftCells, m_WallMaterialId)),

            new(new[]
            {
                L.T("같은 방법으로 나머지 벽도 지어 봅시다.", "Now build the remaining walls the same way."),
            }, () => CellsFixed(m_RightCells, m_WallMaterialId) && CellsFixed(m_FrontCells, m_DoorWallMaterialId)),

            new(Lines(new[]
            {
                L.T("재료는 놓을 곳과 같은 층에 서 있어야 놓을 수 있습니다.\nSpace 를 빠르게 2번 누르면 발밑에 비계가 깔립니다.", "You can only place materials while standing on the same floor.\nDouble-tap Space to lay scaffolding under your feet."),
            }, new[]
            {
                L.T("재료는 놓을 곳과 같은 층에 서 있어야 놓을 수 있습니다.\n점프 버튼을 빠르게 2번 누르면 발밑에 비계가 깔립니다.", "You can only place materials while standing on the same floor.\nDouble-tap the Jump button to lay scaffolding under your feet."),
            }), () =>
            {
                if (GridContract.LocalBuildFloor >= 2) m_ReachedFloor3 = true;
                return m_ReachedFloor3;
            }, () => m_ReachedFloor3 = false),

            new(new[]
            {
                L.T("마지막으로 지붕을 올려 봅시다.", "Finally, let's put the roof on."),
            }, () => CellsPlaced(m_RoofCells, m_RoofMaterialId)),
        };
        return steps;
    }

    private void Update()
    {
        if (!m_Active) return;
        if (m_Index < 0 || m_Index >= m_Steps.Count) return;
        if (GameplayInputBlocker.DialogueBlocked) return;   // 대화 읽는 중엔 퀘스트 판정도 멈춤
        if (m_Steps[m_Index].IsComplete())
            EnterStep(m_Index + 1);
    }

    // 퀘스트 전환이 너무 매끄럽게 느껴지지 않도록, 이전 퀘스트 완료 표시 줄 + 현재 진행도([퀘스트 N/12])를
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
            displayLines.Add(L.T($"<color=#7FE07F><b>퀘스트 {index} 완료!</b></color>", $"<color=#7FE07F><b>Quest {index} complete!</b></color>"));
        for (int i = 0; i < step.Lines.Length; i++)
            displayLines.Add(i == 0
                ? L.T($"<color=#FFD24D><b>[퀘스트 {index + 1}/{m_Steps.Count}]</b></color>\n{step.Lines[i]}", $"<color=#FFD24D><b>[Quest {index + 1}/{m_Steps.Count}]</b></color>\n{step.Lines[i]}")
                : step.Lines[i]);

        // 화면에 남는 마지막 줄 밑에 '정확히 뭘 하면 완료되는지'를 붙인다 — 대사만으론 뭘 하라는 건지 모르겠다는 피드백.
        var goals = MobileControlsHUD.ShouldUseMobileUI ? kGoalsMobile : kGoalsPc;
        if (index < goals.Length)
            displayLines[displayLines.Count - 1] += L.T($"\n<color=#FFD24D><b>목표 : {goals[index]}</b></color>", $"\n<color=#FFD24D><b>Goal : {goals[index]}</b></color>");

        UIManager.Instance.ShowHUDUI<TutorialDialogueHUD>().ShowLines(displayLines, null);
    }

    private void ShowOutro()
    {
        m_Index = -2;
        var displayLines = new List<string> { L.T($"<color=#7FE07F><b>퀘스트 {m_Steps.Count} 완료!</b></color>", $"<color=#7FE07F><b>Quest {m_Steps.Count} complete!</b></color>") };
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

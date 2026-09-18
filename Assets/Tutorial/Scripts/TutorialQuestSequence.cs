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

    private static readonly string[] kIntroLines =
    {
        "반갑습니다.\n당신은 서울의 무너진 명소들을 복구하는 건축 일을 맡게 되었습니다.",
        "일명 '건축 레인저'가 되어 명소도 복구하고,\n짭짤한 일당을 모아 이것저것 구매해 봅시다!",
        "건축은 혼자 진행할 수도 있지만,\n다른 레인저들과 협동하여 진행하면 더욱 수월할 것입니다.",
    };

    // 퀘스트별 완료 조건(BuildSteps의 순서·판정과 반드시 같이 고칠 것) — 각 퀘스트 마지막 줄 밑에 '목표 : …'로 붙는다.
    private static readonly string[] kGoalsPc =
    {
        "W / A / S / D 로 4초 동안 움직이기",
        "우클릭을 누른 채 마우스를 움직여 카메라를 크게 돌리기",
        "우측 하단 휴대폰의 계획도 위에서 우클릭 드래그로 계획도를 크게 돌리기",
        "휴대폰에서 '벽' 카드를 클릭 → [주문!] 버튼 누르기",
        "배송지에 도착한 '벽'에 다가가 클릭해서 들기",
        "벽을 든 채로 G 키를 눌렀다 떼서 던지기",
        "벽을 들고, R 키로 방향을 돌려 맞춘 뒤 반투명한 '왼쪽 벽' 자리에 클릭해서 놓기",
        "망치가 놓인 도구함에 다가가 클릭해서 망치 들기",
        "망치를 든 채 왼쪽 벽에 대고 E 키를 게이지가 찰 때까지 꾹 누르기",
        "오른쪽 벽('벽')과 앞쪽 벽('문이 있는 벽')을 주문·배치하고, 둘 다 망치로 고정하기",
        "스페이스바 2연타로 발밑에 비계를 깔며 3층 높이까지 올라가기",
        "'지붕'을 주문해 들고, 비계로 벽 위 높이까지 올라가 지붕 자리에 놓기",
    };

    private static readonly string[] kGoalsMobile =
    {
        "조이스틱으로 4초 동안 움직이기",
        "빈 화면을 드래그해 카메라를 크게 돌리기",
        "휴대폰 버튼 누르기",
        "휴대폰에서 '벽' 카드를 터치 → [주문!] 버튼 누르기",
        "배송지에 도착한 '벽'에 다가가 터치해서 들기",
        "벽을 든 채로 던지기 버튼을 눌렀다 떼서 던지기",
        "벽을 들고, 회전 버튼으로 방향을 돌려 맞춘 뒤 반투명한 '왼쪽 벽' 자리를 터치해서 놓기",
        "망치가 놓인 도구함에 다가가 터치해서 망치 들기",
        "망치를 든 채 왼쪽 벽 가까이에서 공정 버튼을 게이지가 찰 때까지 꾹 누르기",
        "오른쪽 벽('벽')과 앞쪽 벽('문이 있는 벽')을 주문·배치하고, 둘 다 망치로 고정하기",
        "점프 버튼을 빠르게 2번 눌러 발밑에 비계를 깔며 3층 높이까지 올라가기",
        "'지붕'을 주문해 들고, 비계로 벽 위 높이까지 올라가 지붕 자리에 놓기",
    };

    private static readonly string[] kOutroLines =
    {
        "건축을 얼마나 완벽하게 했는지에 따라,\n완성도가 매겨집니다.",
        "재료를 올바른 곳에 배치하고,\n모든 공정을 완료해야 좋은 점수를 받습니다.\n이 완성도 등급에 따라 건축 후 받는 보수가 달라집니다.",
        "튜토리얼을 마쳤습니다.\n이후 튜토리얼을 다시 진행할 수 있고,\n게임 내에서도 툴팁 안내를 통해 조작키를 확인할 수 있습니다.",
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
                "w / a / s / d 키로 이동합니다.\nshift 키를 누르며 이동하면 달릴 수 있고,\nspace 키를 누르면 점프합니다.",
                "우선, w / a / s / d 를 눌러 움직여 볼까요?",
            }, new[]
            {
                "왼쪽 아래 조이스틱으로 이동합니다.\n조이스틱을 끝까지 밀면 달릴 수 있고,\n점프 버튼을 누르면 점프합니다.",
                "우선, 조이스틱으로 움직여 볼까요?",
            }), () =>
            {
                if (AnyMoveInput()) m_MoveHeldTime += Time.deltaTime;
                return m_MoveHeldTime >= 4f;
            }, () => m_MoveHeldTime = 0f),

            new(Lines(new[]
            {
                "마우스 우클릭을 누른 채 화면을 드래그하면,\n카메라를 돌릴 수 있습니다.",
                "스크롤을 통해 카메라를 확대/축소할 수 있습니다.\n주변을 둘러보세요!",
            }, new[]
            {
                "버튼이 없는 빈 화면을 드래그하면,\n카메라를 돌릴 수 있습니다.",
                "두 손가락을 벌리거나 오므리면 카메라를 확대/축소할 수 있습니다.\n주변을 둘러보세요!",
            }), () =>
            {
                if (m_LocalInput != null && !AnswerPanelFocus.Active)
                    m_CamRotAccum += m_LocalInput.CameraRotate.magnitude;
                return m_CamRotAccum >= kRotateThreshold;
            }, () => m_CamRotAccum = 0f),

            new(Lines(new[]
            {
                "우측 하단 휴대폰엔,\n오늘 지어야 하는 건물의 완공 계획도가 표시됩니다.",
                "계획도에 마우스를 대고 카메라와 동일하게 조작하며 둘러볼 수 있습니다.\n주변을 둘러보세요!",
            }, new[]
            {
                "휴대폰엔,\n오늘 지어야 하는 건물의 완공 계획도가 표시됩니다.",
                "휴대폰 버튼을 눌러 계획도를 열어보세요!",
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
                "건축에 필요한 재료들은 휴대폰에서 주문할 수 있습니다.",
                "완공 계획도에서 원하는 재료를 바로 클릭할 수 있고,\n하단 카탈로그에서 지정해 주문할 수도 있습니다.\n'벽' 재료를 주문해보세요!",
            }, new[]
            {
                "건축에 필요한 재료들은 휴대폰에서 주문할 수 있습니다.",
                "완공 계획도에서 원하는 재료를 바로 터치할 수 있고,\n재료 카탈로그에서 지정해 주문할 수도 있습니다.\n휴대폰 버튼을 눌러 '벽' 재료를 주문해보세요!",
            }), AnyWallPickupExists),

            new(Lines(new[]
            {
                "주문한 재료는 주문 배송지에 도착합니다.",
                "도착한 벽을 클릭해 들어봅시다!",
            }, new[]
            {
                "주문한 재료는 주문 배송지에 도착합니다.",
                "도착한 벽에 가까이 가서 터치해 들어봅시다!",
            }), () => m_LocalCarry.IsHolding),

            new(Lines(new[]
            {
                "G 키를 눌러 손에 든 물건을 던질 수 있습니다.\n팀원과 협동할 때 무척 유용한 기술입니다.",
                "마우스 커서가 향하는 방향으로,\nG 키를 더 오래 누를수록 더 멀리 던집니다.\n'벽' 재료를 던져보세요!",
            }, new[]
            {
                "던지기 버튼을 눌러 손에 든 물건을 던질 수 있습니다.\n팀원과 협동할 때 무척 유용한 기술입니다.",
                "카메라가 보는 방향으로,\n던지기 버튼을 더 오래 누를수록 더 멀리 던집니다.\n'벽' 재료를 던져보세요!",
            }), () => m_ThrewHeldObject, () => m_ThrewHeldObject = false),

            new(Lines(new[]
            {
                "이제 벽을 건축할 곳으로 이동해 배치해봅시다.",
                "오브젝트를 든 채로 R버튼을 누르면 회전시킬 수 있습니다.",
                "벽을 다시 집고,\n투명 답안의 맞는 위치에 클릭해 배치하세요!\n우선 왼쪽 벽부터 배치해봅시다.",
            }, new[]
            {
                "이제 벽을 건축할 곳으로 이동해 배치해봅시다.",
                "오브젝트를 든 채로 회전 버튼을 누르면 회전시킬 수 있습니다.",
                "벽을 다시 집고,\n투명 답안의 맞는 위치를 터치해 배치하세요!\n우선 왼쪽 벽부터 배치해봅시다.",
            }), () => CellsPlaced(m_LeftCells, m_WallMaterialId)),

            new(Lines(new[]
            {
                "답안은 Tab키를 눌러 보이거나 보이지 않게 할 수 있습니다.",
                "배치한 왼쪽 벽 위에 망치 아이콘이 보이시나요?\n해당 아이콘은 이 오브젝트가 '고정' 되어야함을 나타냅니다.",
                "망치 도구를 클릭해 들어보세요.",
            }, new[]
            {
                "답안은 눈 모양 버튼을 눌러 보이거나 보이지 않게 할 수 있습니다.",
                "배치한 왼쪽 벽 위에 망치 아이콘이 보이시나요?\n해당 아이콘은 이 오브젝트가 '고정' 되어야함을 나타냅니다.",
                "망치 도구를 터치해 들어보세요.",
            }), () => m_LocalCarry.IsHoldingTool),

            new(Lines(new[]
            {
                "이런 식으로, 공정이 필요한 오브젝트들이 있습니다.\n두 종류의 공정이 필요한 경우도 있고, 필요하지 않은 경우도 있습니다.",
                "공정을 잘못 진행했을 경우,\nz키를 꾹 누르면 공정 취소가 가능합니다.",
                "망치를 든 채로,\n왼쪽 벽에 E키를 꾹 눌러 망치질을 하면 고정됩니다.",
            }, new[]
            {
                "이런 식으로, 공정이 필요한 오브젝트들이 있습니다.\n두 종류의 공정이 필요한 경우도 있고, 필요하지 않은 경우도 있습니다.",
                "공정을 잘못 진행했을 경우,\n공정취소 버튼을 꾹 누르면 공정 취소가 가능합니다.",
                "망치를 든 채로 왼쪽 벽 가까이에서,\n공정 버튼을 꾹 눌러 망치질을 하면 고정됩니다.",
            }), () => CellsFixed(m_LeftCells, m_WallMaterialId)),

            new(new[]
            {
                "어떤 맵은 이미 약간의 건축이 되어 있거나,\n일부 재료들이 맵 곳곳에 존재하는 경우가 있습니다.",
                "이제 오른쪽 벽과 앞쪽 벽을 알맞게 배치하고 고정해 보세요.",
            }, () => CellsFixed(m_RightCells, m_WallMaterialId) && CellsFixed(m_FrontCells, m_DoorWallMaterialId)),

            new(Lines(new[]
            {
                "이제 지붕이 남았습니다.\n지붕은 '벽 위'에 배치해야 합니다.",
                "하지만 재료를 배치하려면 배치할 곳과 같은 '층'에 위치해야 합니다.\n그럴 때를 대비해 '비계' 오브젝트를 제공합니다.",
                "비계 오브젝트는 무제한으로 제공되며,\n스페이스바를 2번 연타하면 발밑에 깔립니다.",
                "비계 깔기를 통해 3층까지 올라가보세요!",
            }, new[]
            {
                "이제 지붕이 남았습니다.\n지붕은 '벽 위'에 배치해야 합니다.",
                "하지만 재료를 배치하려면 배치할 곳과 같은 '층'에 위치해야 합니다.\n그럴 때를 대비해 '비계' 오브젝트를 제공합니다.",
                "비계 오브젝트는 무제한으로 제공되며,\n점프 버튼을 빠르게 2번 누르면 발밑에 깔립니다.",
                "비계 깔기를 통해 3층까지 올라가보세요!",
            }), () =>
            {
                if (GridContract.LocalBuildFloor >= 2) m_ReachedFloor3 = true;
                return m_ReachedFloor3;
            }, () => m_ReachedFloor3 = false),

            new(new[]
            {
                "지붕을 들고,\n비계 깔기를 통해 한 층 올라가 지붕을 설치해보세요!",
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
            displayLines.Add($"<color=#7FE07F><b>퀘스트 {index} 완료!</b></color>");
        for (int i = 0; i < step.Lines.Length; i++)
            displayLines.Add(i == 0
                ? $"<color=#FFD24D><b>[퀘스트 {index + 1}/{m_Steps.Count}]</b></color>\n{step.Lines[i]}"
                : step.Lines[i]);

        // 화면에 남는 마지막 줄 밑에 '정확히 뭘 하면 완료되는지'를 붙인다 — 대사만으론 뭘 하라는 건지 모르겠다는 피드백.
        var goals = MobileControlsHUD.ShouldUseMobileUI ? kGoalsMobile : kGoalsPc;
        if (index < goals.Length)
            displayLines[displayLines.Count - 1] += $"\n<color=#FFD24D><b>목표 : {goals[index]}</b></color>";

        UIManager.Instance.ShowHUDUI<TutorialDialogueHUD>().ShowLines(displayLines, null);
    }

    private void ShowOutro()
    {
        m_Index = -2;
        var displayLines = new List<string> { $"<color=#7FE07F><b>퀘스트 {m_Steps.Count} 완료!</b></color>" };
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

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 기획서(튜토리얼 기획서.pdf) 원문을 그대로 TutorialQuestData 에셋으로 생성한다.
/// 이미 있으면 덮어쓰지 않고 경고만 띄운다 — 대사를 손으로 다듬었다면 재실행 전에 백업할 것.
/// 벽/지붕 MaterialId는 -1(아무 재료나 인정)로 생성되므로, 실제 MaterialDef를 만든 뒤
/// 생성된 에셋(Assets/Resources/Tutorial/TutorialQuestData.asset)의 인스펙터에서
/// Step 4/5/6/9의 Material Id를 벽 MaterialDef.Id로, Step 11의 Material Id를 지붕 MaterialDef.Id로 바꿔줄 것.
/// </summary>
public static class TutorialQuestDataGenerator
{
    private const string kPath = "Assets/Resources/Tutorial/TutorialQuestData.asset";

    [MenuItem("Jobsnail/Tutorial/Generate Default Tutorial Quest Data")]
    public static void Generate()
    {
        if (File.Exists(kPath))
        {
            Debug.LogWarning($"[TutorialQuestDataGenerator] 이미 존재합니다({kPath}) — 덮어쓰려면 먼저 삭제 후 재실행하세요.");
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(kPath));

        const int wallId = -1;   // TODO(사용자): 벽 MaterialDef.Id로 교체
        const int roofId = -1;   // TODO(사용자): 지붕 MaterialDef.Id로 교체

        var data = ScriptableObject.CreateInstance<TutorialQuestData>();
        data.Steps = new List<TutorialQuestStep>
        {
            // 0. 인트로
            Step(new[]
            {
                "반갑습니다.\n당신은 건축업을 하는 민달팽이입니다.\n당신의 목표는 열심히 일해 달팽이집을 마련하는 것입니다.",
                "건축은 혼자 진행할 수도 있지만,\n다른 민달팽이들과 협동한다면 더욱 수월할 것입니다.",
            }),

            // Quest 1 — 이동 4초
            Step(new[]
            {
                "우선, W / A / S / D 를 눌러 움직여 볼까요?",
                "Shift 키를 누르며 이동하면 달릴 수 있고, Space 키를 누르면 점프합니다.",
            }, TutorialConditionType.MoveAccumulate, targetValue: 4f),

            // Quest 2 — 카메라 180도 회전
            Step(new[]
            {
                "마우스 우클릭을 누른 채 화면을 드래그하면, 카메라를 돌릴 수 있습니다.\n스크롤을 통해 카메라를 확대/축소할 수 있습니다.",
                "주변을 둘러보세요!",
            }, TutorialConditionType.CameraRotateAngle, targetValue: 180f),

            // Quest 3 — 정답 미리보기 180도 회전
            Step(new[]
            {
                "좌측 하단엔, 오늘 지어야 하는 건물의 완성된 모습이 표시됩니다.",
                "정답 UI에 마우스를 대고 카메라와 동일하게 조작하며 둘러볼 수 있습니다.",
            }, TutorialConditionType.AnswerRotateAngle, targetValue: 180f),

            // Quest 4 — 벽 주문
            Step(new[]
            {
                "건축에 필요한 재료들은 우측 드로어 UI에서 주문할 수 있습니다.",
                "드로어 UI는 화살표 버튼을 눌러 접거나 열 수 있습니다.",
                "'벽' 재료를 주문해보세요!",
            }, TutorialConditionType.MaterialOrdered, materialId: wallId),

            // Quest 5 — 벽 줍기
            Step(new[]
            {
                "주문한 재료는 주문 배송지에 도착합니다.",
                "도착한 왼쪽 벽을 클릭해 들어봅시다!",
            }, TutorialConditionType.MaterialPickedUp, materialId: wallId),

            // Quest 6 — 첫 번째 벽 배치
            Step(new[]
            {
                "이제 벽을 건축할 곳으로 운반해 배치해봅시다.",
                "투명 답안의 맞는 위치에 클릭해 배치하세요! 우선 왼쪽 벽부터 배치해봅시다.",
                "오브젝트를 든 채로 R버튼을 누르면 회전시킬 수 있습니다.",
            }, TutorialConditionType.MaterialPlaced, materialId: wallId, targetValue: 1f),

            // Quest 7 — 망치 집기
            Step(new[]
            {
                "답안은 Tab키를 눌러 보이거나 보이지 않게 할 수 있습니다.",
                "배치한 왼쪽 벽 위에 망치 아이콘이 보이시나요? 해당 아이콘은 이 오브젝트가 '고정'되어야 함을 나타냅니다.",
                "망치 도구를 클릭해 들어보세요.",
            }, TutorialConditionType.ToolPickedUp, toolProcess: GridSystem.ProcessType.Fixed),

            // Quest 8 — 첫 번째 벽 고정
            Step(new[]
            {
                "망치를 든 채로, 왼쪽 벽에 E키를 꾹 눌러 망치질을 하면 고정됩니다.",
                "이런 식으로, 공정이 필요한 오브젝트들이 있습니다. 두 개의 공정이 필요한 경우도 있고, 필요하지 않은 경우도 있습니다.",
                "공정을 잘못 진행했을 경우, Z키를 꾹 누르면 공정 취소가 가능합니다.",
            }, TutorialConditionType.MaterialProcessed, toolProcess: GridSystem.ProcessType.Fixed, targetValue: 1f),

            // Quest 9 — 나머지 벽 2개 배치+고정
            Step(new[]
            {
                "(뒷쪽 벽은 맵에 미리 건축해둘 예정)",
                "어떤 맵은 이미 약간의 건축이 되어 있거나, 일부 재료들이 맵 곳곳에 존재하는 경우가 있습니다.",
                "이제 오른쪽 벽과 앞쪽 벽을 알맞게 배치하고 고정해 보세요.",
            }, TutorialConditionType.MaterialProcessed, toolProcess: GridSystem.ProcessType.Fixed, targetValue: 2f),

            // Quest 10 — 비계로 3층
            Step(new[]
            {
                "마지막으로, 이제 지붕이 남았습니다.",
                "민달팽이는 원하는 벽 앞에서 W키를 누르면 벽을 기어오를 수 있습니다. 하지만 벽을 기어올라가 건축하기에 공간이 부족할 때가 있습니다.",
                "그럴 때를 대비해 '비계' 오브젝트를 제공합니다. 비계 오브젝트는 무제한으로 제공되며, 스페이스바를 2번 연타하면 자동으로 바닥에 깔립니다.",
                "비계 깔기를 통해 3층까지 올라가보세요!",
            }, TutorialConditionType.ScaffoldFloorReached, targetValue: 3f),

            // Quest 11 — 지붕 설치
            Step(new[]
            {
                "지붕을 들고, 비계 깔기를 통해 한 층 올라가 지붕을 설치해보세요!",
            }, TutorialConditionType.MaterialPlaced, materialId: roofId, targetValue: 1f),

            // 아웃트로
            Step(new[]
            {
                "튜토리얼을 마쳤습니다. 이후 튜토리얼을 다시 진행할 수 있고, 게임 내에서 툴팁 UI를 통해 조작키를 확인할 수 있습니다.",
                "등껍질을 장만하는 그날까지 열심히 건축합시다!",
            }),
        };

        AssetDatabase.CreateAsset(data, kPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[TutorialQuestDataGenerator] 생성 완료 → {kPath}\n" +
                  "벽/지붕 MaterialDef를 만든 뒤 이 에셋의 Step 4/5/6/9(벽)·Step 11(지붕) Material Id를 채워주세요.");
    }

    private static TutorialQuestStep Step(string[] lines,
        TutorialConditionType condition = TutorialConditionType.None,
        int materialId = -1,
        GridSystem.ProcessType toolProcess = GridSystem.ProcessType.Fixed,
        float targetValue = 1f)
    {
        return new TutorialQuestStep
        {
            Lines = lines,
            Condition = condition,
            MaterialId = materialId,
            ToolProcess = toolProcess,
            TargetValue = targetValue,
        };
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>튜토리얼 한 스텝의 완료 조건 종류. TutorialManager가 이 값을 보고 어떤 이벤트/누적치를 감시할지 결정한다.</summary>
public enum TutorialConditionType
{
    None,                 // 조건 없음 — 대사만 보여주고 마지막 줄 확인 즉시 다음 스텝
    MoveAccumulate,       // WASD 이동 누적 시간(초) >= TargetValue
    CameraRotateAngle,    // 메인(플레이어) 카메라 누적 회전각(도) >= TargetValue
    AnswerRotateAngle,    // 정답 미리보기 카메라 누적 회전각(도) >= TargetValue
    MaterialOrdered,      // MaterialId 재료를 주문
    MaterialPickedUp,     // MaterialId 재료를 손에 듦
    ToolPickedUp,         // 도구(망치=Fixed/페인트=Painted, ToolProcess로 지정)를 손에 듦
    MaterialPlaced,       // MaterialId 재료를 배치 — TargetValue회 누적
    MaterialProcessed,    // ToolProcess 공정을 완료 — TargetValue회 누적
    ScaffoldFloorReached, // 비계로 TargetValue층 이상 도달
}

[Serializable]
public class TutorialQuestStep
{
    [Tooltip("텍스트박스에 순서대로 표시할 대사. 한 줄씩 클릭/엔터로 넘어간다(기획서의 '-' 구분과 1:1).")]
    [TextArea(1, 3)]
    public string[] Lines = Array.Empty<string>();

    [Tooltip("대사를 모두 넘긴 뒤 대기할 완료 조건. None이면 대사 종료 즉시 다음 스텝으로.")]
    public TutorialConditionType Condition = TutorialConditionType.None;

    [Tooltip("재료 관련 조건에서 대상 재료(MaterialDef.Id). -1이면 아무 재료나 인정.")]
    public int MaterialId = -1;

    [Tooltip("공정 관련 조건에서 대상 공정. ToolPickedUp/MaterialProcessed에서만 사용.")]
    public GridSystem.ProcessType ToolProcess = GridSystem.ProcessType.Fixed;

    [Tooltip("조건 목표값 — 이동 누적초 / 회전 누적각도 / 도달 층수 / 필요 횟수 등, 조건마다 의미가 다르다.")]
    public float TargetValue = 1f;
}

/// <summary>
/// 튜토리얼 전체 대본. Steps[0]이 인트로(보통 Condition=None), 마지막 스텝이 아웃트로.
/// 실제 데이터는 Assets/Resources/Tutorial/TutorialQuestData.asset 하나만 사용(Resources.Load로 읽음).
/// 메뉴 Jobsnail ▸ Tutorial ▸ Generate Default Tutorial Quest Data 로 기획서 원문 그대로 생성 가능.
/// </summary>
[CreateAssetMenu(fileName = "TutorialQuestData", menuName = "Tutorial/Quest Data")]
public class TutorialQuestData : ScriptableObject
{
    public List<TutorialQuestStep> Steps = new();
}

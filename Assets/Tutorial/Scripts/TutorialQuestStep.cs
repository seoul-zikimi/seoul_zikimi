using System;

/// <summary>한 퀘스트의 대사 줄과 완료 판정. 완료 판정은 기존 게임플레이 스크립트를 건드리지 않고
/// 이미 공개된 상태값만 읽어서 수행한다(TutorialQuestSequence 참고).</summary>
public class TutorialQuestStep
{
    public readonly string[] Lines;
    public readonly Func<bool> IsComplete;
    public readonly Action OnEnter;

    public TutorialQuestStep(string[] lines, Func<bool> isComplete, Action onEnter = null)
    {
        Lines = lines;
        IsComplete = isComplete;
        OnEnter = onEnter;
    }
}

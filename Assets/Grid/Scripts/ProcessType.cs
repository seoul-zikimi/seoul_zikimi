using System;

namespace GridSystem
{
    /// <summary>
    /// 블록에 수행하는 건축 공정. [Flags]라 한 셀에 여러 공정을 비트로 누적할 수 있다.
    /// 완료 상태는 int 비트마스크로 저장 → 네트워크 전송·채점 비교가 단순해진다.
    /// </summary>
    [Flags]
    public enum ProcessType
    {
        None    = 0,
        Fixed   = 1 << 0,  // 고정 (망치)
        Painted = 1 << 1,  // 페인트칠 (페인트통)
    }
    
    /// <summary>공정의 정규 순서. 앞에서부터 순차로만 적용된다(고정 → 페인트).</summary>
    public static class ProcessOrder
    {
        public static readonly ProcessType[] Sequence =
        {
            ProcessType.Fixed,
            ProcessType.Painted,
        };
    }
}
namespace GridSystem
{
    /// <summary>
    /// (B) RuntimeGrid 한 셀의 상태. 값 타입(struct) — Dictionary 값으로 보유하고
    /// 수정 시 get-modify-set 으로 통째 교체한다.
    /// </summary>
    public struct CellState
    {
        public bool occupied;
        public int materialId;
        public int rotationStep;          // 0~3 (0/90/180/270° about Y)
        public int completedProcessMask;  // [Flags] ProcessType 비트마스크
        public ulong ownerObjectId;       // 멀티칸 오브젝트의 앵커 참조(같은 오브젝트의 셀들이 공유)

        /// <summary>비어있는 셀. materialId를 NoMaterial로 둬 default(materialId=0)과의 혼동 방지.</summary>
        public static CellState Empty => new CellState
        {
            occupied = false,
            materialId = MaterialCatalog.NoMaterial,
            rotationStep = 0,
            completedProcessMask = 0,
            ownerObjectId = 0,
        };
    }
}

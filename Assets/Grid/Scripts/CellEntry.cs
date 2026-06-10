using System;
using Unity.Netcode;
using UnityEngine;

namespace GridSystem
{
    /// <summary>
    /// NetworkList로 복제되는 셀 1칸 상태. (멀티칸 오브젝트는 칸마다 항목 1개)
    /// NetworkList 원소 조건: INetworkSerializable + IEquatable(변경 감지용) + 언매니지드 친화.
    /// </summary>
    public struct CellEntry : INetworkSerializable, IEquatable<CellEntry>
    {
        public Vector3Int cell;
        public int materialId;
        public byte rotationStep;
        public int completedProcessMask;
        public ulong ownerObjectId;

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            // Vector3Int는 컴포넌트로 직렬화(버전 호환·안전).
            int cx = cell.x, cy = cell.y, cz = cell.z;
            s.SerializeValue(ref cx);
            s.SerializeValue(ref cy);
            s.SerializeValue(ref cz);
            cell = new Vector3Int(cx, cy, cz);

            s.SerializeValue(ref materialId);
            s.SerializeValue(ref rotationStep);
            s.SerializeValue(ref completedProcessMask);
            s.SerializeValue(ref ownerObjectId);
        }

        public bool Equals(CellEntry o)
            => cell == o.cell
            && materialId == o.materialId
            && rotationStep == o.rotationStep
            && completedProcessMask == o.completedProcessMask
            && ownerObjectId == o.ownerObjectId;

        public override bool Equals(object obj) => obj is CellEntry o && Equals(o);
        public override int GetHashCode() => cell.GetHashCode() ^ (materialId * 397) ^ completedProcessMask;
    }
}

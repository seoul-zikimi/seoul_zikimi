using System;
using Unity.Netcode;
using UnityEngine;

namespace GridSystem
{
    /// <summary>
    /// 바닥에 떨어진 재료 1개(복제). 붕괴/버리기/철거 결과물 — 그리드 CellEntry 패턴과 동일하게
    /// NetworkList로 서버→전원 동기화하고, 클라는 로컬 비주얼을 재구성한다.
    /// </summary>
    public struct PickupEntry : INetworkSerializable, IEquatable<PickupEntry>
    {
        public ulong pickupId;     // 고유 id(서버 발급)
        public int materialId;     // 도구 픽업이면 -1
        public int toolBit;        // 0=재료 / 그 외=도구(ProcessType 비트: 1=망치(고정), 2=페인트통)
        public Vector3 pos;        // 바닥 안착 위치(서버 권위, 줍기 판정 기준)
        public Vector3 fromPos;    // 낙하 시작 위치(로컬 '노답중력' 연출용)

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref pickupId);
            s.SerializeValue(ref materialId);
            s.SerializeValue(ref toolBit);
            s.SerializeValue(ref pos);
            s.SerializeValue(ref fromPos);
        }

        public bool Equals(PickupEntry o)
            => pickupId == o.pickupId && materialId == o.materialId && toolBit == o.toolBit
            && pos == o.pos && fromPos == o.fromPos;
    }
}

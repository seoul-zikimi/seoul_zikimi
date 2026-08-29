using System.Collections.Generic;
using UnityEngine;

namespace GridSystem
{
    /// <summary>
    /// 그리드 시스템의 계약(인터페이스) 계층 — 2026-08-30 도입(ARCHITECTURE.md §1 원칙의 Grid 적용).
    ///
    /// 목적: GridNetwork(1,200줄)·MaterialDropField 구체 클래스 직결합을 끊고,
    /// ① 소비자가 필요한 능력만 보게(읽기 전용 소비자가 서버 조작 API를 못 건드림)
    /// ② 테스트에서 Fake 구현으로 대체 가능하게(GameplayFramework·Weather와 같은 체계).
    ///
    /// 채택 규약: 새 소비자는 구체 타입 대신 여기 인터페이스로 필드를 선언한다.
    /// (씬에서 찾을 땐 GetComponent&lt;GridNetwork&gt;() 등 구체 타입으로 찾고, 필드에는 인터페이스로 담는다.)
    /// 기존 소비자는 손대는 김에 점진 전환 — 일괄 치환은 하지 않는다(불필요한 diff·충돌 방지).
    /// </summary>

    /// <summary>그리드 '읽기' 계약 — 복제 상태 기준이라 서버·클라 모두 호출 가능.</summary>
    public interface IGridState
    {
        /// <summary>협동(또는 팀A) 점수 스냅샷.</summary>
        ScoreSnapshot Score { get; }
        /// <summary>협동(또는 팀A) 완성률(0~100).</summary>
        float ScorePercent { get; }
        /// <summary>팀별 점수(0=팀A, 1=팀B).</summary>
        ScoreSnapshot ScoreFor(int team);

        /// <summary>해당 셀이 비어 있는지 — 배치 전 사전 검사용.</summary>
        bool IsCellFree(Vector3Int cell);
        /// <summary>셀의 재료 id·완료 공정 마스크. 빈 셀이면 false.</summary>
        bool TryGetCell(Vector3Int cell, out int materialId, out int completedMask);
        /// <summary>cell이 속한 블록이 차지한 셀 전체를 result에 채운다(진입 시 Clear). 블록 없으면 false.</summary>
        bool TryGetBlockCells(Vector3Int cell, List<Vector3Int> result);
        /// <summary>이 셀의 블록을 회수(F)할 수 있는가.</summary>
        bool IsPickupable(Vector3Int cell);
        /// <summary>cell을 덮는 블록 비주얼 루트(스퀴시 등 연출용). 없으면 null.</summary>
        GameObject VisualAt(Vector3Int cell);

        /// <summary>셀 변경이 실제 반영(더티 flush)된 프레임에 1회 발화 — 폴링 소비자의 더티 게이트용.</summary>
        event System.Action CellsChanged;
    }

    /// <summary>그리드 '클라 요청' 계약 — 오너 로컬에서 서버로 보내는 조작 요청(Rpc 래퍼).</summary>
    public interface IGridRequests
    {
        void RequestPlace(Vector3Int anchor, int materialId, byte rot);
        void RequestRemove(Vector3Int cell);
        void RequestProcess(Vector3Int cell, int processBit, bool apply);
        /// <summary>외부 충격(플레이어 부딪힘 등) — 지지 재검사 트리거.</summary>
        void RequestShock(Vector3Int cell);
        void RequestCancelLast(Vector3Int cell);
    }

    /// <summary>그리드 '서버 권위' 계약 — IsServer에서만 유효. 기믹·아이템 시스템용.</summary>
    public interface IGridServerOps
    {
        bool ServerPickupBlock(Vector3Int cell, out int materialId);
        void ServerCollectCells(List<CellEntry> into);
        /// <summary>블록 소각(경복궁 화마). 반환: 태운 셀 수.</summary>
        int ServerBurnBlock(Vector3Int cell);
        int ServerEarthquake(int team);
        bool ServerCannonDestroy(int team);
        int ServerWindCollapse(int team, int count);
        void ServerAddBonus(int points, int team = 0);
        void RecomputeScore();
    }

    /// <summary>바닥 재료(픽업) 필드 계약 — 배송·던지기·물길 급송·킥·줍기의 단일 창구.</summary>
    public interface IPickupField
    {
        // ── 서버 권위 ──
        void ServerDrop(int materialId, Vector3 fromPos);
        void ServerThrow(int materialId, Vector3 fromPos, Vector3 toPos);
        /// <summary>배송 스폰(높이 존중). 반환: pickupId(0=실패) — 케이블카 미수령 회수 등 추적용.</summary>
        ulong ServerDeliver(int materialId, Vector3 fromPos, Vector3 toPos);
        bool ServerRemove(ulong pickupId);
        /// <summary>픽업을 dir로 distance만큼 흘려보냄(물길 급송). 있었으면 true.</summary>
        bool ServerFloat(ulong pickupId, Vector3 dir, float distance);
        bool TryGetPickupPos(ulong pickupId, out Vector3 pos);
        void ServerCollectPickups(List<PickupEntry> into);
        void ServerReset();

        // ── 클라 요청(Rpc 래퍼) ──
        void RequestDrop(int materialId, Vector3 fromPos);
        void RequestThrow(int materialId, Vector3 fromPos, Vector3 toPos);
        void RequestThrowTool(int toolBit, Vector3 fromPos, Vector3 toPos);
        void RequestKick(ulong pickupId, Vector3 dir);
        void RequestGrab(ulong pickupId);

        // ── 공용 조회 ──
        /// <summary>범위 내 픽업 (id, pos) 수집(재사용 리스트 — 진입 시 Clear).</summary>
        void CollectWithin(Vector3 from, float range, List<ulong> ids, List<Vector3> positions);
    }
}

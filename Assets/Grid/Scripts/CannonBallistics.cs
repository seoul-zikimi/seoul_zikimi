using System;
using System.Collections.Generic;
using UnityEngine;

namespace GridSystem
{
    /// <summary>
    /// 대포 탄도 계산. 서버의 명중 판정(GridNetwork.ServerCannonDestroy)과
    /// 클라이언트의 예상 궤적(PlayerCarry)이 반드시 같은 식을 써야 하므로 한 곳에 모았다.
    /// 45° 고정 발사각이라 사거리 R = v²/g, 즉 충전량이 곧 사거리다.
    /// </summary>
    public static class CannonBallistics
    {
        public const float MinRange = 3f;    // 최소 충전 사거리(m)
        public const float MaxRange = 17f;   // 최대 충전 사거리 — 분할벽 근처에서 상대 진영 안쪽까지
        public const float Gravity = 20f;
        /// <summary>총구 높이 — 발도, 조준 화살표도 이 높이(플레이어 몸통 중간)에서 나간다.</summary>
        public const float MuzzleHeight = 0.9f;

        private const float kMarchStep = 0.02f;   // 탄도 적분 간격(초) — 셀 1칸(1m)보다 촘촘해야 관통이 없다
        private const float kMaxFlight = 12f;     // 안전 상한(초)

        public static float RangeFor(float charge01) => Mathf.Lerp(MinRange, MaxRange, Mathf.Clamp01(charge01));

        /// <summary>수평·수직 초속(45°이므로 둘이 같다).</summary>
        public static void Velocity(float charge01, out float vh, out float vy)
        {
            float speed = Mathf.Sqrt(RangeFor(charge01) * Gravity);
            vh = speed * 0.70710678f;   // cos45 = sin45
            vy = vh;
        }

        /// <summary>조준 입력을 수평 단위벡터로 정규화(길이 0이면 앞쪽).</summary>
        public static Vector3 FlatDir(Vector3 aim)
        {
            aim.y = 0f;
            return aim.sqrMagnitude > 1e-4f ? aim.normalized : Vector3.forward;
        }

        public static Vector3 PointAt(Vector3 origin, Vector3 dir, float vh, float vy, float t)
        {
            Vector3 p = origin + dir * (vh * t);
            p.y = origin.y + vy * t - 0.5f * Gravity * t * t;
            return p;
        }

        /// <summary>
        /// 포물선을 적분해 처음 부딪히는 셀을 찾는다. 맞으면 true(hitCell 유효),
        /// 아무것도 못 맞고 땅에 떨어지면 false + landPoint에 착탄 지점.
        /// occupied는 호출자가 넘긴다 — 서버는 자기 그리드, 클라는 복제 상태를 본다.
        /// path가 주어지면 궤적 점을 채워준다(예상 궤적 그리기용).
        /// </summary>
        public static bool March(Vector3 origin, Vector3 dir, float charge01, float groundY,
                                 Func<Vector3Int, bool> occupied,
                                 out Vector3Int hitCell, out Vector3 landPoint,
                                 List<Vector3> path = null)
        {
            dir = FlatDir(dir);
            Velocity(charge01, out float vh, out float vy);

            hitCell = Vector3Int.zero;
            landPoint = origin + dir * RangeFor(charge01);
            landPoint.y = groundY;

            path?.Clear();
            path?.Add(origin);

            for (float t = kMarchStep; t < kMaxFlight; t += kMarchStep)
            {
                Vector3 p = PointAt(origin, dir, vh, vy, t);

                if (p.y <= groundY)   // 땅에 떨어짐 = 불발
                {
                    landPoint = new Vector3(p.x, groundY, p.z);
                    path?.Add(landPoint);
                    return false;
                }

                path?.Add(p);

                var cell = GridCoordinates.WorldToCell(p);
                if (occupied != null && occupied(cell))
                {
                    hitCell = cell;
                    landPoint = GridCoordinates.CellToWorld(cell) + Vector3.one * (GridContract.Unit * 0.5f);
                    return true;
                }
            }
            return false;
        }
    }
}

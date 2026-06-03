using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;
using Unity.Netcode;
using DG.Tweening;

namespace Player
{
    [UnityEngine.RequireComponent(typeof(UnityEngine.Rigidbody))]
    public class PlayerBounce : MonoBehaviour
    {
        // prefab별 풀
        private static readonly Dictionary<GameObject, IObjectPool<GameObject>> s_Pools
            = new Dictionary<GameObject, IObjectPool<GameObject>>();

        // 인스턴스별 ParticleSystem[] 캐시 — OnGetFromPool alloc 제거
        private static readonly Dictionary<GameObject, ParticleSystem[]> s_ParticleSystems
            = new Dictionary<GameObject, ParticleSystem[]>();

        // duration별 WaitForSeconds 캐시 — 코루틴 alloc 제거
        private static readonly Dictionary<float, WaitForSeconds> s_WaitCache
            = new Dictionary<float, WaitForSeconds>();

        private Rigidbody m_Rb;
        private PlayerConfigSO m_Config;
        private Transform m_Body;
        private bool m_IsBouncing;
        private DG.Tweening.TweenCallback m_OnBounceComplete; // 람다 캐싱 — bounce마다 alloc 방지

        public bool IsBouncing => m_IsBouncing;
        public System.Action OnBounce;

        // 권한 보유 측(owner)이 충돌을 감지하면 다른 클라이언트로 피드백을 복제하도록 PlayerUnit이 주입.
        // (contactPoint, spawnSharedParticle)
        public System.Action<Vector3, bool> OnBounceReplicate;

        public void Init(PlayerConfigSO config)
        {
            m_Rb               = GetComponent<Rigidbody>();
            m_Config           = config;
            m_Body             = transform.Find("Body");
            m_OnBounceComplete = () => m_IsBouncing = false; // 1회만 alloc
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (m_IsBouncing) return;
            if (!collision.gameObject.CompareTag("Player")) return;
            // 네트워크 경로: 자신이 Owner인 머신에서만 자기 충돌을 처리한다.
            // 비권한 측에서도 OnCollisionEnter가 발생하므로 여기서 걸러 중복/오작동을 막는다.
            if (!HasBounceAuthority()) return;

            var contact = collision.GetContact(0); // ContactPoint[] alloc 없음
            Vector3 bounceDir = contact.normal;
            bounceDir.y = 0;
            if (bounceDir == Vector3.zero) return;

            m_IsBouncing = true;

            // 수평 반동 (물리) — owner의 dynamic Rigidbody에만 적용
            m_Rb.linearVelocity = Vector3.zero;
            m_Rb.AddForce(bounceDir.normalized * m_Config.ReboundForce, ForceMode.Impulse);

            // 충돌 파티클은 양쪽 중 한 쪽만 스폰 (NetworkObjectId 비교 — 머신 간 일관)
            bool spawnSharedParticle = ShouldOwnSharedFX(collision.gameObject);

            // owner 로컬 즉시 피드백 (반응성)
            PlayBounceFeedback(contact.point, spawnSharedParticle);
            // 나머지 클라이언트로 멀티캐스트 (PlayerUnit이 ClientRpc로 복제).
            // 테스트(비네트워크) 경로에선 PlayerUnit이 IsSpawned 체크로 no-op → 로컬 재생만 남는다.
            OnBounceReplicate?.Invoke(contact.point, spawnSharedParticle);

            OnBounce?.Invoke();
        }

        /// <summary>충돌 피드백(바디 팝업 + 사운드 + 파티클). 멀티캐스트로 모든 클라이언트가 동일하게 호출.</summary>
        public void PlayBounceFeedback(Vector3 point, bool spawnSharedParticle)
        {
            PlayBodyPunch();
            if (SoundManager.Instance != null) // 씬에 SoundManager 없는 클라 방어
                SoundManager.Instance.PlaySFXAt(SFXType.PlayerBounce, point);
            if (spawnSharedParticle)
                SpawnBounceEffect(point);
        }

        // 오버쿡드 스타일 팝업 (비주얼만)
        private void PlayBodyPunch()
        {
            if (m_Body != null)
            {
                m_Body.DOKill();
                m_Body.DOPunchPosition(
                    Vector3.up * m_Config.BounceHeight,
                    m_Config.BounceDuration, 1, 0.5f)
                    .OnComplete(m_OnBounceComplete);
                m_Body.DOPunchScale(
                    new Vector3(-0.15f, 0.35f, -0.15f),
                    m_Config.BounceDuration, 1, 0.5f);
            }
            else
            {
                m_IsBouncing = false;
            }
        }

        private void SpawnBounceEffect(Vector3 point)
        {
            var prefab = m_Config.GetBounceEffectPrefab();
            if (prefab == null) return;

            var pool = GetOrCreatePool(prefab);
            var fx   = pool.Get();
            fx.transform.position = point;
            StartCoroutine(ReleaseAfter(fx, pool, m_Config.BounceEffectDuration));
        }

        // 네트워크 경로: Owner인 머신에서만 true. 테스트(비네트워크) 경로: 항상 true.
        private bool HasBounceAuthority()
        {
            var nob = GetComponent<NetworkObject>();
            return nob == null || !nob.IsSpawned || nob.IsOwner;
        }

        // 충돌 파티클을 어느 쪽이 스폰할지 — 양쪽 중복 방지
        private bool ShouldOwnSharedFX(GameObject other)
        {
            var myNob    = GetComponent<NetworkObject>();
            var otherNob = other.GetComponent<NetworkObject>();

            // 네트워크 경로: NetworkObjectId는 모든 머신에서 동일 → 일관된 dedup
            if (myNob != null && otherNob != null && myNob.IsSpawned && otherNob.IsSpawned)
                return myNob.NetworkObjectId > otherNob.NetworkObjectId;

            // 테스트(비네트워크) 경로: InstanceID로 dedup
            return gameObject.GetInstanceID() > other.GetInstanceID();
        }

        // ── Pool ─────────────────────────────────────────────

        private IEnumerator ReleaseAfter(GameObject go, IObjectPool<GameObject> pool, float delay)
        {
            // WaitForSeconds 캐싱 — duration당 1회 alloc
            if (!s_WaitCache.TryGetValue(delay, out var wait))
            {
                wait = new WaitForSeconds(delay);
                s_WaitCache[delay] = wait;
            }
            yield return wait;
            if (go != null) // CFXR 자동소멸 방어
                pool.Release(go);
        }

        private static IObjectPool<GameObject> GetOrCreatePool(GameObject prefab)
        {
            if (s_Pools.TryGetValue(prefab, out var existing)) return existing;

            var pool = new ObjectPool<GameObject>(
                createFunc: () =>
                {
                    var go = Object.Instantiate(prefab);

                    // ParticleSystem[] 캐싱 — OnGetFromPool에서 재사용
                    var systems = go.GetComponentsInChildren<ParticleSystem>(true);
                    s_ParticleSystems[go] = systems;

                    // CFXR stopAction=Destroy 차단
                    foreach (var ps in systems)
                    {
                        var m = ps.main;
                        m.stopAction = ParticleSystemStopAction.None;
                    }
                    // CFXR_Effect clearBehavior=Destroy 차단 — 진짜 자멸 원인
                    foreach (var effect in go.GetComponentsInChildren<CartoonFX.CFXR_Effect>(true))
                    {
                        effect.clearBehavior = CartoonFX.CFXR_Effect.ClearBehavior.None;
                    }
                    return go;
                },
                actionOnGet:     OnGetFromPool,
                actionOnRelease: OnReleaseToPool,
                actionOnDestroy: go =>
                {
                    s_ParticleSystems.Remove(go); // 캐시 정리
                    Object.Destroy(go);
                },
                collectionCheck: false,
                defaultCapacity: 4,
                maxSize:         10
            );
            s_Pools[prefab] = pool;
            return pool;
        }

        // static 메서드 레퍼런스 → 풀 생성 시 1회만 Action<T> 변환
        private static void OnGetFromPool(GameObject go)
        {
            go.SetActive(true);
            if (s_ParticleSystems.TryGetValue(go, out var systems))
            {
                foreach (var ps in systems)
                {
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    ps.Play();
                }
            }
        }

        private static void OnReleaseToPool(GameObject go)
        {
            go.SetActive(false);
        }
    }
}

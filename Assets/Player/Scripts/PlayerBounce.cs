using UnityEngine;
using DG.Tweening;

namespace Player
{
    [UnityEngine.RequireComponent(typeof(UnityEngine.Rigidbody))]
    public class PlayerBounce : MonoBehaviour
    {
        private Rigidbody m_Rb;
        private PlayerConfigSO m_Config;
        private Transform m_Body;
        private bool m_IsBouncing;
        private DG.Tweening.TweenCallback m_OnBounceComplete; // 람다 캐싱 — bounce마다 alloc 방지

        public System.Action OnBounce;

        public void Init(PlayerConfigSO config)
        {
            m_Rb               = GetComponent<Rigidbody>();
            m_Config           = config;
            m_Body             = transform.Find("Body");
            m_OnBounceComplete = () => m_IsBouncing = false; // 1회만 할당
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (m_IsBouncing) return;
            if (!collision.gameObject.CompareTag("Player")) return;

            Vector3 bounceDir = collision.GetContact(0).normal;
            bounceDir.y = 0;
            if (bounceDir == Vector3.zero) return;

            m_IsBouncing = true;

            // 수평 반동 (물리)
            m_Rb.linearVelocity = Vector3.zero;
            m_Rb.AddForce(bounceDir.normalized * m_Config.ReboundForce, ForceMode.Impulse);

            // 오버쿡드 스타일 팝업 (비주얼만)
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

            OnBounce?.Invoke();
        }
    }
}
using System;
using UnityEngine;

namespace Player.Test
{
    [RequireComponent(typeof(Rigidbody))]
    public class GatherDriver : MonoBehaviour
    {
        private Rigidbody m_Rb;
        private Vector3 m_Target;
        private float m_Speed;

        public void Begin(Vector3 target, float speed)
        {
            m_Rb = GetComponent<Rigidbody>();
            m_Target = target;
            m_Speed = speed;
        }

        private void FixedUpdate()
        {
            Vector3 dir = m_Target-transform.position;
            dir.y = 0;
            m_Rb.linearVelocity=dir.normalized*m_Speed;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (collision.gameObject.CompareTag("Player"))
            {
                Destroy(this);
            }
        }
    }
}
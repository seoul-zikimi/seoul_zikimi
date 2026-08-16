using UnityEngine;

namespace Kamgam.HitMe
{
    public class KeyboardMovement : MonoBehaviour
    {
        public float Speed = 10f;

        protected Vector3 _startPos;

        public void Start()
        {
            _startPos = transform.localPosition;
        }

        public void Update()
        {
            var pos = transform.position;

            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))
            {
                pos += new Vector3(-Speed * Time.deltaTime, 0f, 0f);
            }
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow))
            {
                pos += new Vector3(0f, 0f, -Speed * Time.deltaTime);
            }
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow))
            {
                pos += new Vector3(Speed * Time.deltaTime, 0f, 0f);
            }
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow))
            {
                pos += new Vector3(0f, 0f, Speed * Time.deltaTime);
            }

            transform.position = pos;
        }

    }
}

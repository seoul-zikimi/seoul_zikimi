using UnityEngine;

namespace Kamgam.HitMe
{
    public class CircularMovement : MonoBehaviour
    {
        public float Speed = 2f;
        public float Radius = 10f;
        public Vector3 CenterOffset = new Vector3(0,0,0);

        protected Vector3 _startPos;
        protected float _progress;

        public void Start()
        {
            _startPos = transform.localPosition;
        }

        public void Update()
        {
            _progress += Time.deltaTime * Speed;
            var x = Mathf.Sin(_progress) * Radius;
            var z = Mathf.Cos(_progress) * Radius;

            var pos = _startPos - (CenterOffset * Radius) + new Vector3(x, 0f, z);
            transform.localPosition = pos;
        }

    }
}

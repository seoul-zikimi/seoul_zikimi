using UnityEngine;

namespace GridSystem
{
    /// <summary>
    /// 천천히 도는 장식용 회전 — DDP 야경 서치라이트 빔 등.
    /// 물리·네트워크 무관, 각 클라 로컬로만 돈다(시작 위상은 배치 회전으로 어긋나게 둔다).
    /// </summary>
    public class SlowSpin : MonoBehaviour
    {
        [Tooltip("초당 회전(오일러). 서치라이트는 y만 주면 된다.")]
        public Vector3 DegreesPerSecond = new Vector3(0f, 9f, 0f);

        private void Update()
        {
            transform.Rotate(DegreesPerSecond * Time.deltaTime, Space.Self);
        }
    }
}

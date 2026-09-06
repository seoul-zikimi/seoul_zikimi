using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 붙어 있는 오브젝트에서 클릭 이벤트를 삼켜 부모로 전파되지 않게 한다.
/// 팝업 패널처럼 "바깥을 누르면 닫히는" 오버레이 안쪽 영역에 사용한다.
/// </summary>
public sealed class JobsnailClickBlocker : MonoBehaviour, IPointerClickHandler
{
    public void OnPointerClick(PointerEventData eventData) { }
}

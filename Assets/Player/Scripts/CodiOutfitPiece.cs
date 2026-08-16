using UnityEngine;

/// <summary>아웃핏 조각 — 어느 본에, 본 기준 어떤 상대 포즈로 붙는지 기록.
/// 오프셋·크기는 캐릭터 루트 스케일로 정규화 → 리그 100배 스케일·애니메이션 자세와 무관하게 재현.
/// 주의: 프리팹 직렬화 때문에 반드시 클래스명과 같은 파일에 있어야 함.</summary>
public class CodiOutfitPiece : MonoBehaviour
{
    public string BoneName;

    public Vector3 BonePos;                            // 본 회전 기준 오프셋(루트 스케일 정규화)
    public Quaternion BoneRot = Quaternion.identity;   // 본 기준 상대 회전
    public Vector3 WorldScale = Vector3.one;           // 루트 스케일 1 기준 월드 크기
    public int Version;                                // 3 = 본 상대 포즈(현행)
}

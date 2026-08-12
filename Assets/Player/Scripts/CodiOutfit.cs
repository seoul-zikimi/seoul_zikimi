using UnityEngine;

/// <summary>
/// 코디 아웃핏(의상 세트) — 에디터에서 손으로 맞춘 조각(모자·조끼 등)의 본·로컬값을 그대로 저장한 프리팹.
/// Resources/CodiOutfits/&lt;id&gt;.prefab (제작: 드레스업한 캐릭터 선택 → Tools ▸ MyPage ▸ Extract Outfit).
/// 적용 = 조각을 같은 본에 같은 로컬값으로 붙이기 → 어떤 model.fbx 인스턴스(프리뷰·인게임)든 동일하게 입음.
/// </summary>
public class CodiOutfit : MonoBehaviour
{
    [Tooltip("옷장에 표시될 이름")] public string DisplayName = "의상";
    [Tooltip("가격(코인). 0 = 무료")] public int Price = 100;
    [Tooltip("대상 캐릭터 id — 빈 문자열 = 달팽이, char_turtle / char_crab 등")] public string TargetCharacter = "";
    [Tooltip("옷장 슬롯 바닥에 깔릴 썸네일(비면 UI_pngs/MyPage/Thumb_<id> 자동 탐색)")] public Sprite Thumbnail;
    [Tooltip("착용 시 크기 여유 — 애니메이션 스쿼시로 몸이 옷을 뚫고 나오는 것 방지"), Range(1f, 1.3f)] public float ScaleMargin = 1.06f;

    /// <summary>썸네일 — 직접 지정 없으면 이름 컨벤션(Resources/UI_pngs/MyPage/Thumb_id)으로 탐색.</summary>
    public Sprite ResolveThumbnail()
        => Thumbnail != null ? Thumbnail : Resources.Load<Sprite>("UI_pngs/MyPage/Thumb_" + name);

    private const string kMark = "~Outfit_";

    /// <summary>캐릭터에 아웃핏 입히기(기존 아웃핏은 벗김). id 비면 벗기만 함.
    /// characterId = 이 캐릭터의 선택 캐릭터(빈 문자열=달팽이, null=플레이어에서 자동 감지).
    /// 아웃핏의 TargetCharacter와 다르면 입히지 않는다(본 구조가 달라 허공에 뜨는 것 방지).</summary>
    public static void Apply(GameObject character, string id, string characterId = null)
    {
        RemoveAll(character);
        if (character == null || string.IsNullOrEmpty(id)) return;
        var prefab = Resources.Load<GameObject>("CodiOutfits/" + id);
        if (prefab == null) { Debug.LogWarning($"[Outfit] 프리팹 없음: Resources/CodiOutfits/{id}"); return; }

        characterId ??= CharacterSwap.CurrentId(character);
        var target = prefab.GetComponent<CodiOutfit>();
        if ((target != null ? target.TargetCharacter : "") != characterId) return;   // 다른 캐릭터용 아웃핏 — 벗기만 함

        // 루트 = "현재 보이는 모델"의 Animator 노드 — 인게임에서 대체 캐릭터(거북/게)가 활성일 때
        // 숨은 달팽이 캐리어를 잡으면 루트 스케일이 어긋나 아웃핏 크기가 틀어진다.
        var anim = CharacterSwap.ActiveAnimator(character);
        Transform root = anim != null ? anim.transform : character.transform;
        var meta = prefab.GetComponent<CodiOutfit>();
        float margin = meta != null ? Mathf.Max(1f, meta.ScaleMargin) : 1f;

        float rs = root.lossyScale.x;   // 캐릭터 루트의 균등 스케일(프리뷰·인게임 1)

        foreach (var piece in prefab.GetComponentsInChildren<CodiOutfitPiece>())
        {
            if (piece.Version < 3)
            {
                Debug.LogWarning($"[Outfit] 구버전 아웃핏 데이터({id}) — PreviewSnail 선택 후 Extract Outfit 재실행 필요");
                continue;
            }
            var bone = FindBone(character.transform, piece.BoneName);
            if (bone == null) { Debug.LogWarning($"[Outfit] 본 없음: {piece.BoneName}"); continue; }

            var inst = Instantiate(piece.gameObject);
            inst.name = kMark + id;
            var tr = inst.transform;
            // 본 기준 상대 포즈로 재현(루트 스케일 정규화) — 애니메이션 어느 순간에 입혀도 어긋나지 않음
            tr.SetParent(bone, worldPositionStays: false);
            Vector3 ws = piece.WorldScale * (rs * margin);   // 목표 월드 크기(+뚫림 방지 여유)
            Vector3 bl = bone.lossyScale;
            tr.localScale = new Vector3(ws.x / bl.x, ws.y / bl.y, ws.z / bl.z);
            tr.position = bone.position + bone.rotation * (piece.BonePos * rs);
            tr.rotation = bone.rotation * piece.BoneRot;
        }
    }

    /// <summary>캐릭터의 아웃핏 조각 전부 제거.</summary>
    public static void RemoveAll(GameObject character)
    {
        if (character == null) return;
        var list = new System.Collections.Generic.List<GameObject>();
        foreach (var t in character.GetComponentsInChildren<Transform>(true))
            if (t.name.StartsWith(kMark)) list.Add(t.gameObject);
        foreach (var go in list)
        {
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }
    }

    /// <summary>Resources/CodiOutfits 전체 카탈로그(이름순).</summary>
    public static System.Collections.Generic.List<CodiOutfit> Catalog()
    {
        var list = new System.Collections.Generic.List<CodiOutfit>();
        foreach (var go in Resources.LoadAll<GameObject>("CodiOutfits"))
        {
            var o = go.GetComponent<CodiOutfit>();
            if (o != null) list.Add(o);
        }
        list.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.Ordinal));
        return list;
    }

    private static Transform FindBone(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }
}

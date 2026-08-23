#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Player.EditorTools
{
    /// <summary>
    /// 거북이·게 아웃핏 자동 생성 (Tools ▸ Character ▸ 아웃핏 생성(거북·게)):
    /// Assets/Player/Outfits/*.glb 를 캐릭터 프리팹(Idle 첫 프레임 포즈)에 맞춰 자동 배치하고
    /// 달팽이 아웃핏과 같은 포맷(CodiOutfit + CodiOutfitPiece, 본 상대 포즈 v3)으로
    /// Resources/CodiOutfits/&lt;id&gt;.prefab 을 만든다. 재실행해도 이름·가격·썸네일은 보존.
    /// 위치가 어색하면 아래 테이블의 scale/offset을 조정해 재실행.
    /// </summary>
    public static class CharacterOutfitBuilder
    {
        const string kSrcDir = "Assets/Player/Outfits";
        const string kDstDir = "Assets/Resources/CodiOutfits";

        enum Kind { Shell, Cloth }

        // charId, 아웃핏 id, 표시 이름, glb 파일명, 종류, 폭 배율, 로컬 오프셋(캐릭터 기준: x=우, y=상, z=앞)
        static readonly (string charId, string id, string display, string file, Kind kind, float widthScale, Vector3 offset)[] kItems =
        {
            ("char_turtle", "shell_rainbow_turtle", "무지개 등딱지", "turtle_rainbowshell", Kind.Shell, 0.95f, new Vector3(0f, 0.05f, -0.16f)),
            ("char_turtle", "shell_star_turtle",    "별 등딱지",    "turtle_starshell",    Kind.Shell, 0.95f, new Vector3(0f, 0.05f, -0.16f)),
            ("char_turtle", "cloth_hoodie_turtle",  "후드티",       "turtle_hodie",        Kind.Cloth, 1.12f, new Vector3(0f, 0.02f, 0f)),
            ("char_turtle", "skin_vest_turtle",     "안전 세트",    "turtle_vest",         Kind.Cloth, 1.10f, new Vector3(0f, 0.04f, 0f)),
            ("char_crab",   "shell_rainbow_crab",   "무지개 소라",  "crab_rainbowshell",   Kind.Shell, 0.80f, new Vector3(0f, 0.10f, -0.14f)),
            ("char_crab",   "shell_star_crab",      "별 소라",      "crab_starshell",      Kind.Shell, 0.80f, new Vector3(0f, 0.10f, -0.14f)),
            ("char_crab",   "cloth_hoodie_crab",    "후드티",       "crab_hodie",          Kind.Cloth, 1.10f, new Vector3(0f, 0.02f, 0f)),
            ("char_crab",   "skin_vest_crab",       "안전 세트",    "crab_vest",           Kind.Cloth, 1.08f, new Vector3(0f, 0.04f, 0f)),
        };

        [MenuItem("Tools/Character/아웃핏 생성(거북·게)")]
        static void Build()
        {
            int made = 0;
            foreach (var it in kItems)
            {
                if (BuildOne(it.charId, it.id, it.display, it.file, it.kind, it.widthScale, it.offset)) made++;
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[OutfitBuilder] 완료 — {made}/{kItems.Length}개 생성. 어긋난 건 테이블 배율/오프셋 조정 후 재실행.");
        }

        // ── 데롱데롱 머리띠(하트/똥/새싹) — 전 캐릭터 스킨 ─────────────
        static readonly (string design, string display)[] kHairbands =
        {
            ("heart", "하트 머리띠"), ("poop", "똥 머리띠"), ("sprout", "새싹 머리띠"),
        };

        [MenuItem("Tools/Character/머리띠 생성(전 캐릭터)")]
        static void BuildHairbands()
        {
            foreach (var (design, display) in kHairbands)
            {
                // 거북·게: 머리 본 기준 자동 배치
                BuildHatOutfit("char_turtle", $"skin_hairband_{design}_turtle", display, $"hairband_{design}", 0.62f);
                BuildHatOutfit("char_crab", $"skin_hairband_{design}_crab", display, $"hairband_{design}", 0.50f);
                // 달팽이: 안전모 조각의 본 데이터를 재활용해 같은 자리에
                BuildSnailHat($"skin_hairband_{design}", display, $"hairband_{design}", 0.9f);
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[OutfitBuilder] 머리띠 9종 생성 완료(달팽이·거북·게 × 하트/똥/새싹)");
        }

        // 거북·게 머리 위 배치(안전모와 같은 방식, 머리띠는 머리에 조금 더 얹힘)
        static void BuildHatOutfit(string charId, string id, string display, string file, float widthScale)
        {
            var itemModel = AssetDatabase.LoadAssetAtPath<GameObject>($"{kSrcDir}/{file}.glb");
            var charPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Resources/Characters/{charId}.prefab");
            if (itemModel == null || charPrefab == null) { Debug.LogWarning($"[OutfitBuilder] {id} 스킵(에셋 없음)"); return; }

            var ch = (GameObject)PrefabUtility.InstantiatePrefab(charPrefab);
            var item = (GameObject)PrefabUtility.InstantiatePrefab(itemModel);
            GameObject outfitRoot = null;
            try
            {
                string animDir = charId == "char_turtle" ? "Assets/Player/Turtle" : "Assets/Player/Crab";
                AnimationClip idle = null;
                foreach (var a in AssetDatabase.LoadAllAssetsAtPath($"{animDir}/Idle.fbx"))
                    if (a is AnimationClip c && !c.name.StartsWith("__preview")) { idle = c; break; }
                var anim = ch.GetComponentInChildren<Animator>(true);
                if (idle != null && anim != null) idle.SampleAnimation(anim.gameObject, 0f);

                var head = FindBone(ch.transform, "mixamorig:Head");
                if (head == null) { Debug.LogError($"[OutfitBuilder] {charId} Head 본 없음"); return; }

                var cb = Bounds(ch);
                var ib0 = Bounds(item);
                float s = cb.size.x * widthScale / Mathf.Max(ib0.size.x, 0.001f);
                item.transform.localScale = Vector3.one * s;
                var ib = Bounds(item);
                // 밴드 아랫단이 정수리에 살짝 겹치게
                var hc = new Vector3(cb.center.x, cb.max.y + ib.extents.y * 0.15f, head.position.z);
                item.transform.position += hc - ib.center;
                item.name = "Item_" + file;

                outfitRoot = new GameObject(id);
                var meta = outfitRoot.AddComponent<CodiOutfit>();
                meta.DisplayName = display;
                meta.Price = 400;
                meta.TargetCharacter = charId;
                var old = AssetDatabase.LoadAssetAtPath<GameObject>($"{kDstDir}/{id}.prefab");
                var oldMeta = old != null ? old.GetComponent<CodiOutfit>() : null;
                if (oldMeta != null) { meta.DisplayName = oldMeta.DisplayName; meta.Price = oldMeta.Price; meta.Thumbnail = oldMeta.Thumbnail; meta.ScaleMargin = oldMeta.ScaleMargin; }

                var poseRoot = anim != null ? anim.transform : ch.transform;
                RecordPiece(outfitRoot, item, head, poseRoot.lossyScale.x);

                if (old != null) AssetDatabase.DeleteAsset($"{kDstDir}/{id}.prefab");
                PrefabUtility.SaveAsPrefabAsset(outfitRoot, $"{kDstDir}/{id}.prefab");
                Debug.Log($"[OutfitBuilder] {id} 저장");
            }
            finally
            {
                Object.DestroyImmediate(item);
                Object.DestroyImmediate(ch);
                if (outfitRoot != null) Object.DestroyImmediate(outfitRoot);
            }
        }

        // 달팽이: skin_safetyset의 안전모 조각(본 상대 데이터)을 복제해 머리띠로 스왑
        static void BuildSnailHat(string id, string display, string file, float widthRatio)
        {
            var itemModel = AssetDatabase.LoadAssetAtPath<GameObject>($"{kSrcDir}/{file}.glb");
            var snailSet = AssetDatabase.LoadAssetAtPath<GameObject>($"{kDstDir}/skin_safetyset.prefab");
            var helmetSrc = snailSet != null ? FindChild(snailSet.transform, "Item_Hat_SafetyHelmet") : null;
            var helmetPiece = helmetSrc != null ? helmetSrc.GetComponent<CodiOutfitPiece>() : null;
            if (itemModel == null || helmetPiece == null) { Debug.LogWarning($"[OutfitBuilder] {id} 스킵(달팽이 안전모 참조 없음)"); return; }

            GameObject outfitRoot = null;
            GameObject probeH = null, probeI = null;
            try
            {
                // 월드 폭 계산용 임시 인스턴스
                probeH = Object.Instantiate(helmetSrc.gameObject);
                probeH.transform.localScale = Vector3.one;
                float helmetW = Bounds(probeH).size.x;
                probeI = (GameObject)PrefabUtility.InstantiatePrefab(itemModel);
                float bandW = Bounds(probeI).size.x;
                // 헬멧과 같은 월드 폭 × 비율이 되도록 스케일
                float scale = helmetPiece.WorldScale.x * (helmetW / Mathf.Max(bandW, 0.001f)) * widthRatio;

                outfitRoot = new GameObject(id);
                var meta = outfitRoot.AddComponent<CodiOutfit>();
                meta.DisplayName = display;
                meta.Price = 400;
                meta.TargetCharacter = "";   // 달팽이(기본 캐릭터)
                var old = AssetDatabase.LoadAssetAtPath<GameObject>($"{kDstDir}/{id}.prefab");
                var oldMeta = old != null ? old.GetComponent<CodiOutfit>() : null;
                if (oldMeta != null) { meta.DisplayName = oldMeta.DisplayName; meta.Price = oldMeta.Price; meta.Thumbnail = oldMeta.Thumbnail; meta.ScaleMargin = oldMeta.ScaleMargin; }

                var piece = (GameObject)PrefabUtility.InstantiatePrefab(itemModel, outfitRoot.transform);
                piece.name = "Item_" + file;
                var cp = piece.AddComponent<CodiOutfitPiece>();
                cp.BoneName = helmetPiece.BoneName;
                cp.BonePos = helmetPiece.BonePos;
                cp.BoneRot = helmetPiece.BoneRot;
                cp.WorldScale = Vector3.one * scale;
                cp.Version = 3;

                if (old != null) AssetDatabase.DeleteAsset($"{kDstDir}/{id}.prefab");
                PrefabUtility.SaveAsPrefabAsset(outfitRoot, $"{kDstDir}/{id}.prefab");
                Debug.Log($"[OutfitBuilder] {id} 저장(달팽이, 안전모 자리 기준)");
            }
            finally
            {
                if (probeH != null) Object.DestroyImmediate(probeH);
                if (probeI != null) Object.DestroyImmediate(probeI);
                if (outfitRoot != null) Object.DestroyImmediate(outfitRoot);
            }
        }

        [MenuItem("Tools/Character/아웃핏 생성(거북 후드만)")]
        static void BuildTurtleHoodieOnly() => BuildHoodieOnly("cloth_hoodie_turtle");

        [MenuItem("Tools/Character/아웃핏 생성(게 후드만)")]
        static void BuildCrabHoodieOnly() => BuildHoodieOnly("cloth_hoodie_crab");

        static void BuildHoodieOnly(string id)
        {
            foreach (var it in kItems)
                if (it.id == id)
                { BuildOne(it.charId, it.id, it.display, it.file, it.kind, it.widthScale, it.offset); break; }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[OutfitBuilder] {id} 재생성 완료");
        }

        static bool BuildOne(string charId, string id, string display, string file, Kind kind, float widthScale, Vector3 offset)
        {
            var itemModel = AssetDatabase.LoadAssetAtPath<GameObject>($"{kSrcDir}/{file}.glb");
            if (itemModel == null) { Debug.LogWarning($"[OutfitBuilder] {kSrcDir}/{file}.glb 없음 — 스킵"); return false; }
            var charPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Resources/Characters/{charId}.prefab");
            if (charPrefab == null) { Debug.LogError($"[OutfitBuilder] 캐릭터 프리팹 없음: {charId}"); return false; }

            var ch = (GameObject)PrefabUtility.InstantiatePrefab(charPrefab);
            GameObject outfitRoot = null;
            var item = (GameObject)PrefabUtility.InstantiatePrefab(itemModel);
            try
            {
                // Idle 첫 프레임 포즈로 고정(런타임과 같은 자세 기준)
                string animDir = charId == "char_turtle" ? "Assets/Player/Turtle" : "Assets/Player/Crab";
                AnimationClip idle = null;
                foreach (var a in AssetDatabase.LoadAllAssetsAtPath($"{animDir}/Idle.fbx"))
                    if (a is AnimationClip c && !c.name.StartsWith("__preview")) { idle = c; break; }
                var anim = ch.GetComponentInChildren<Animator>(true);
                if (idle != null && anim != null) idle.SampleAnimation(anim.gameObject, 0f);

                var bone = FindBone(ch.transform, "mixamorig:Spine1") ?? FindBone(ch.transform, "mixamorig:Spine");
                if (bone == null) { Debug.LogError($"[OutfitBuilder] {charId}에 mixamorig:Spine 본 없음"); return false; }

                var cb = Bounds(ch);
                var ib0 = Bounds(item);

                // 폭 맞춤(캐릭터 폭 × 배율)
                float s = cb.size.x * widthScale / Mathf.Max(ib0.size.x, 0.001f);
                item.transform.localScale = Vector3.one * s;
                var ib = Bounds(item);

                // 위치: Shell = 등(뒤로 밀착), Cloth = 몸통 중앙. 캐릭터는 +Z가 정면.
                Vector3 center;
                if (kind == Kind.Shell)
                    center = new Vector3(cb.center.x, bone.position.y, cb.min.z - ib.extents.z * 0.25f);
                else
                    center = new Vector3(cb.center.x, bone.position.y, cb.center.z);
                center += offset * cb.size.y;   // 캐릭터 키 비례 오프셋

                item.transform.position += center - ib.center;
                item.name = "Item_" + file;

                // 달팽이 추출기와 같은 포맷으로 조각 기록
                outfitRoot = new GameObject(id);
                var meta = outfitRoot.AddComponent<CodiOutfit>();
                meta.DisplayName = display;
                meta.Price = 100;
                meta.TargetCharacter = charId;

                string path = $"{kDstDir}/{id}.prefab";
                var old = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var oldMeta = old != null ? old.GetComponent<CodiOutfit>() : null;
                if (oldMeta != null)
                {
                    meta.DisplayName = oldMeta.DisplayName;
                    meta.Price = oldMeta.Price;
                    meta.Thumbnail = oldMeta.Thumbnail;
                    meta.ScaleMargin = oldMeta.ScaleMargin;
                }

                var poseRoot = anim != null ? anim.transform : ch.transform;
                float rootScale = poseRoot.lossyScale.x;
                RecordPiece(outfitRoot, item, bone, rootScale);

                // 안전 세트: 달팽이 안전모 메시를 재활용해 머리(mixamorig:Head)에 얹는다
                if (id.StartsWith("skin_vest"))
                {
                    var snailSet = AssetDatabase.LoadAssetAtPath<GameObject>($"{kDstDir}/skin_safetyset.prefab");
                    var helmetSrc = snailSet != null ? FindChild(snailSet.transform, "Item_Hat_SafetyHelmet") : null;
                    var head = FindBone(ch.transform, "mixamorig:Head");
                    if (helmetSrc != null && head != null)
                    {
                        var helmet = Object.Instantiate(helmetSrc.gameObject);
                        Object.DestroyImmediate(helmet.GetComponent<CodiOutfitPiece>());   // 옛 조각 데이터 제거(새로 기록)
                        helmet.name = "Item_Hat_SafetyHelmet";
                        var hb0 = Bounds(helmet);
                        float hs = cb.size.x * (charId == "char_crab" ? 0.55f : 0.70f) / Mathf.Max(hb0.size.x, 0.001f);
                        helmet.transform.localScale = Vector3.one * hs;
                        helmet.transform.rotation = Quaternion.identity;
                        var hb = Bounds(helmet);
                        // 정수리에 얹기 — 헬멧 아랫단이 머리 꼭대기에 살짝 겹치게
                        var hc = new Vector3(cb.center.x, cb.max.y + hb.extents.y * 0.45f, head.position.z);
                        helmet.transform.position += hc - hb.center;
                        RecordPiece(outfitRoot, helmet, head, rootScale);
                        Object.DestroyImmediate(helmet);
                    }
                    else Debug.LogWarning("[OutfitBuilder] 달팽이 안전모(Item_Hat_SafetyHelmet) 또는 Head 본을 못 찾음 — 조끼만 생성");
                }

                System.IO.Directory.CreateDirectory(kDstDir);
                if (old != null) AssetDatabase.DeleteAsset(path);
                PrefabUtility.SaveAsPrefabAsset(outfitRoot, path);
                Debug.Log($"[OutfitBuilder] {id} → 본 '{bone.name}' 저장 완료");
                return true;
            }
            finally
            {
                Object.DestroyImmediate(item);
                Object.DestroyImmediate(ch);
                if (outfitRoot != null) Object.DestroyImmediate(outfitRoot);
            }
        }

        // 조각을 달팽이 추출기와 같은 포맷(본 상대 포즈 v3)으로 기록
        static void RecordPiece(GameObject outfitRoot, GameObject item, Transform bone, float rootScale)
        {
            var piece = Object.Instantiate(item, outfitRoot.transform);
            piece.name = item.name;
            var cp = piece.GetComponent<CodiOutfitPiece>();
            if (cp == null) cp = piece.AddComponent<CodiOutfitPiece>();
            cp.BoneName = bone.name;
            cp.BonePos = Quaternion.Inverse(bone.rotation) * (item.transform.position - bone.position) / rootScale;
            cp.BoneRot = Quaternion.Inverse(bone.rotation) * item.transform.rotation;
            cp.WorldScale = item.transform.lossyScale / rootScale;
            cp.Version = 3;
        }

        static Transform FindChild(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        static Transform FindBone(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        static Bounds Bounds(GameObject go)
        {
            var b = new Bounds(go.transform.position, Vector3.zero);
            bool first = true;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (first) { b = r.bounds; first = false; }
                else b.Encapsulate(r.bounds);
            }
            return b;
        }
    }
}
#endif

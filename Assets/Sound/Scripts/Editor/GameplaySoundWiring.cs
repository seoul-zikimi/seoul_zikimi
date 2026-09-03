using UnityEditor;
using UnityEngine;

/// <summary>
/// 아이템·날씨·맵 기믹 사운드 17종을 SoundLibrary.asset에 원클릭 연결(멱등 — 있으면 클립만 갱신).
/// 클립: Assets/Sound/Clips/Gameplay/ 아래 아래 표의 파일명(.wav/.mp3/.ogg 아무거나).
///
/// 클립이 아직 없는 항목은 건너뛴다 — 연결 전엔 호출부가 각자 폴백으로 동작한다:
///   · 아이템 계열: ItemFx의 기존 합성음(뾰롱/펑/스윕)이 그대로 남
///   · 지진: 착지음(LandObject) 폴백
///   · 날씨 루프·수문·퍼레이드·미끄덩: 무음(클립 연결 순간부터 들림)
/// 즉 사운드팀은 파일을 폴더에 넣고 이 메뉴만 다시 누르면 된다.
/// </summary>
public static class GameplaySoundWiring
{
    const string k_LibraryPath = "Assets/Sound/Data/SoundLibrary.asset";
    const string k_ClipDir     = "Assets/Sound/Clips/Gameplay";

    static readonly string[] k_Extensions = { "wav", "mp3", "ogg" };

    static readonly (SFXType type, string clip, string desc)[] k_Wires =
    {
        // ── 아이템 공통 ──
        (SFXType.ItemBoxSpawn,       "Item_Box_Spawn",       "상자 등장 뾰롱"),
        (SFXType.ItemPickup,         "Item_Pickup",          "상자 획득 뾰롱"),
        (SFXType.ItemUse,            "Item_Use",             "발동 공통 스윕(전용음 없는 종류)"),
        // ── 아이템 종류별 발동음 ──
        (SFXType.ItemCannonFire,     "Item_Cannon_Fire",     "대포 펑~"),
        (SFXType.ItemEarthquake,     "Item_Earthquake",      "지진 쿠르릉(돌 구르는 소리)"),
        (SFXType.ItemOrderHack,      "Item_Order_Hack",      "주문 해킹 삐리릭 오류음"),
        (SFXType.ItemSlowdown,       "Item_Slowdown",        "속도/공정 저하 하강음(띠로리, 8bit)"),
        (SFXType.ItemSpeedup,        "Item_Speedup",         "속도/공정 상승 상승음"),
        (SFXType.ItemFog,            "Item_Fog",             "안개 피유융 연막 바람소리"),
        // ── 날씨 ──
        (SFXType.WeatherRainLoop,    "Weather_Rain_Loop",    "빗소리 루프"),
        (SFXType.WeatherWindLoop,    "Weather_Wind_Loop",    "강풍 바람소리 루프"),
        (SFXType.WeatherTyphoonLoop, "Weather_Typhoon_Loop", "태풍(비+바람) 루프"),
        (SFXType.WeatherSlip,        "Weather_Slip",         "미끄덩 킹받는 소리"),
        // ── 맵 기믹 ──
        (SFXType.DdpFloodWarning,    "Ddp_Flood_Warning",    "DDP 수문 개방 경보"),
        (SFXType.DdpFloodLoop,       "Ddp_Flood_Loop",       "DDP 물 콸콸 루프"),
        (SFXType.LotteParadeFanfare, "Lotte_Parade_Fanfare", "롯월 퍼레이드 예고 팡파레"),
        (SFXType.LotteParadeMusic,   "Lotte_Parade_Music",   "롯월 퍼레이드 행진곡 루프"),
    };

    [MenuItem("Tools/Sound/아이템·날씨·맵 사운드 연결")]
    static void Apply()
    {
        var lib = AssetDatabase.LoadAssetAtPath<SoundLibrarySO>(k_LibraryPath);
        if (lib == null) { Debug.LogError($"[게임플레이사운드] SoundLibrary 없음: {k_LibraryPath}"); return; }

        if (!AssetDatabase.IsValidFolder(k_ClipDir))
            AssetDatabase.CreateFolder("Assets/Sound/Clips", "Gameplay");

        var so = new SerializedObject(lib);
        var entries = so.FindProperty("sfxEntries");
        int wired = 0, missing = 0;
        foreach (var (type, clipName, desc) in k_Wires)
        {
            var clip = FindClip(clipName);
            if (clip == null)
            {
                Debug.LogWarning($"[게임플레이사운드] 클립 없음(건너뜀): {k_ClipDir}/{clipName}.(wav|mp3|ogg) — {desc}");
                missing++;
                continue;
            }

            int idx = -1;   // 기존 엔트리 찾기(같은 type)
            for (int i = 0; i < entries.arraySize; i++)
                if (entries.GetArrayElementAtIndex(i).FindPropertyRelative("type").intValue == (int)type) { idx = i; break; }
            if (idx < 0) { idx = entries.arraySize; entries.arraySize++; }

            var e = entries.GetArrayElementAtIndex(idx);
            e.FindPropertyRelative("type").intValue = (int)type;
            var clips = e.FindPropertyRelative("clips");
            clips.arraySize = 1;
            clips.GetArrayElementAtIndex(0).objectReferenceValue = clip;
            wired++;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(lib);
        AssetDatabase.SaveAssets();
        Debug.Log($"[게임플레이사운드] 완료 ✔ 연결 {wired}건 / 클립 없음 {missing}건. " +
                  "클립을 채운 뒤 이 메뉴를 다시 누르면 나머지가 연결됩니다.");
    }

    static AudioClip FindClip(string clipName)
    {
        foreach (var ext in k_Extensions)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{k_ClipDir}/{clipName}.{ext}");
            if (clip != null) return clip;
        }
        return null;
    }
}

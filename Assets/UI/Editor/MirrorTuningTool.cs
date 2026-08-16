using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 거울면 맞추기 워크플로 — 플레이 중 SurfaceAnchor를 움직여 유리에 맞춘 뒤(실시간 반영),
/// 정지하고 Tools ▸ MyPage ▸ Apply Mirror Tuning 을 누르면 플레이 중 값이 씬에 저장된다.
/// (플레이 중 MirrorReflection이 매 프레임 PlayerPrefs에 앵커 값을 백업해 둠)
/// </summary>
public static class MirrorTuningTool
{
    private const string kKey = "MyPage_MirrorTuning";

    [MenuItem("Tools/MyPage/Apply Mirror Tuning (플레이 중 맞춘 값 저장)")]
    private static void Apply()
    {
        if (Application.isPlaying)
        {
            EditorUtility.DisplayDialog("거울 튜닝", "플레이를 먼저 정지한 뒤 눌러 주세요.", "확인");
            return;
        }
        if (!PlayerPrefs.HasKey(kKey))
        {
            EditorUtility.DisplayDialog("거울 튜닝", "저장된 튜닝 값이 없어요.\n플레이 중 SurfaceAnchor를 움직여 맞춘 뒤 정지하고 다시 눌러 주세요.", "확인");
            return;
        }

        var mirror = Object.FindFirstObjectByType<MirrorReflection>();
        if (mirror == null)
        {
            EditorUtility.DisplayDialog("거울 튜닝", "씬에 MirrorReflection이 없어요. MyPage 씬을 연 상태에서 실행해 주세요.", "확인");
            return;
        }
        var anchor = mirror.transform.Find("SurfaceAnchor");
        if (anchor == null)
        {
            EditorUtility.DisplayDialog("거울 튜닝", "거울에 SurfaceAnchor 자식이 없어요.", "확인");
            return;
        }

        var t = JsonUtility.FromJson<MirrorReflection.MirrorTuning>(PlayerPrefs.GetString(kKey));
        Undo.RecordObject(anchor, "Apply Mirror Tuning");
        anchor.position = t.pos;                       // 월드 값 그대로
        anchor.rotation = Quaternion.Euler(t.rot);
        anchor.localScale = t.scale;
        EditorSceneManager.MarkSceneDirty(anchor.gameObject.scene);
        Debug.Log($"[MirrorTuning] 적용 완료 — pos {t.pos}, rot {t.rot}, scale {t.scale}. 씬 저장(Ctrl+S)하면 확정!");
    }
}

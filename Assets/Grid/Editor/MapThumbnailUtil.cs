using System.IO;
using UnityEditor;
using UnityEngine;

namespace GridSystem.EditorTools
{
    /// <summary>맵 썸네일 촬영(프리팹을 지하에 세워 촬영) — 맵 생성 툴들이 공용으로 쓴다.</summary>
    public static class MapThumbnailUtil
    {
        public static Sprite Capture(GameObject prefab, string pngPath)
        {
            GameObject inst = null, camGo = null;
            RenderTexture rt = null;
            try
            {
                inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                inst.transform.position = new Vector3(0f, -5000f, 0f);

                var rends = inst.GetComponentsInChildren<Renderer>();
                if (rends.Length == 0) return null;
                var b = rends[0].bounds;
                foreach (var r in rends) b.Encapsulate(r.bounds);

                camGo = new GameObject("~ThumbCam");
                var cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.Skybox;
                cam.fieldOfView = 40f;
                float dist = b.size.magnitude * 0.75f;
                cam.transform.position = b.center + new Vector3(1f, 0.8f, -1f).normalized * dist;
                cam.transform.LookAt(b.center);
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = dist * 4f;

                rt = new RenderTexture(512, 512, 24);
                cam.targetTexture = rt;
                cam.Render();

                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(512, 512, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 512, 512), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;

                File.WriteAllBytes(pngPath, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(pngPath);
                var imp = (TextureImporter)AssetImporter.GetAtPath(pngPath);
                imp.textureType = TextureImporterType.Sprite;
                imp.spriteImportMode = SpriteImportMode.Single;
                imp.SaveAndReimport();
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(pngPath);
                if (sprite == null)
                    foreach (var a in AssetDatabase.LoadAllAssetsAtPath(pngPath))
                        if (a is Sprite s) { sprite = s; break; }
                return sprite;
            }
            finally
            {
                if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); }
                if (camGo != null) Object.DestroyImmediate(camGo);
                if (inst != null) Object.DestroyImmediate(inst);
            }
        }
    }
}

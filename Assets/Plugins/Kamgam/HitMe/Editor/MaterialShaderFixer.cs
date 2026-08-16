#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Kamgam.HitMe
{
    public static class MaterialShaderFixer
    {
        public class MaterialInfo
        {
            public Color Color;
            public Texture MainTexture;
            public Vector2Int Tiling;

            public MaterialInfo(Color color, Texture mainTexture, Vector2Int tiling)
            {
                Color = color;
                MainTexture = mainTexture;
                Tiling = tiling;
            }

            public MaterialInfo(Color color, Texture mainTexture)
            {
                Color = color;
                MainTexture = mainTexture;
                Tiling = Vector2Int.one;
            }

            public MaterialInfo(Color color)
            {
                Color = color;
                MainTexture = null;
                Tiling = Vector2Int.one;
            }
        }

        public enum RenderPiplelineType
        {
            URP, HDRP, BuiltIn
        }

        static System.Action _onComplete;

        #region StartFixMaterial delayed
        static double startFixingAt;

        public static void FixMaterialsDelayed(System.Action onComplete)
        {
            // Materials may not be loaded at this time. Thus we wait for them to be imported.
            _onComplete = onComplete;
            EditorApplication.update -= onEditorUpdate;
            EditorApplication.update += onEditorUpdate;
            startFixingAt = EditorApplication.timeSinceStartup + 3; // wait N seconds
        }

        static void onEditorUpdate()
        {
            // wait for the time to reach startPackageImportAt
            if (startFixingAt - EditorApplication.timeSinceStartup < 0)
            {
                EditorApplication.update -= onEditorUpdate;
                try
                {
                    FixMaterials();
                }
                finally
                {
                    _onComplete?.Invoke();
                }
                return;
            }
        }
        #endregion

        [MenuItem("Tools/Hit Me/Debug/Fix Materials")]
        public static void FixMaterials()
        {
            RenderPiplelineType createdForRenderPipleline = RenderPiplelineType.BuiltIn;
            var currentRenderPipline = GetCurrentRenderPiplelineType();

            Debug.Log("Upgrading materials from " + createdForRenderPipleline + " to " + currentRenderPipline);

            // Revert to the standard shader of each render pipeline and apply the color.
            if (currentRenderPipline != createdForRenderPipleline)
            {
                // Get the default shader for the currently used render pipeline asset
                var shader = GetDefaultShader();
                if (shader != null)
                {
                    var materialPaths = new Dictionary<string, MaterialInfo>
                    {
                         { "Assets/Kamgam/HitMe/Examples/Materials/Standard.mat", new MaterialInfo( HexToColor("ffffff") ) }
                        ,{ "Assets/Kamgam/HitMe/Examples/Materials/Ground.mat", new MaterialInfo( HexToColor("118403") ) }
                        ,{ "Assets/Kamgam/HitMe/Examples/Materials/Red.mat", new MaterialInfo( HexToColor("FF000A") ) }
                        ,{ "Assets/Kamgam/HitMe/Examples/Materials/Yellow.mat", new MaterialInfo( HexToColor("FFF800") ) }
                        ,{ "Assets/Kamgam/HitMe/Examples/Materials/Blue.mat", new MaterialInfo( HexToColor("00D4FF") ) }
                        ,{ "Assets/Kamgam/HitMe/Examples/Materials/Brown.mat", new MaterialInfo( HexToColor("783302") ) }
                    };

                    foreach (var kv in materialPaths)
                    {
                        var path = kv.Key;
                        var info = kv.Value;
                        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
                        if (material != null)
                        {
                            Debug.Log($"Setting material '{path}' to Standard shader.");
                            material.shader = shader;
                            material.color = info.Color;
                            if (info.MainTexture != null)
                            {
                                if (material.HasTexture("_Main"))
                                    material.SetTexture("_Main", info.MainTexture);
                                if (material.HasTexture("_MainTex"))
                                    material.SetTexture("_MainTex", info.MainTexture);
                                if (material.HasTexture("_BaseMap"))
                                    material.SetTexture("_BaseMap", info.MainTexture);
                            }
                            material.mainTextureScale = info.Tiling;
                        }
                    }
                    AssetDatabase.SaveAssets();
                }
                else
                {
                    Debug.LogError("No default shader found! Please contact support.");
                }
            }
            else
            {
                Debug.Log("All good, no material to fix.");
            }
        }

        public static Color HexToColor(string hex)
        {
            if (hex[0] == '#')
            {
                hex = hex.Substring(1);
            }

            Color color = new Color();

            if (hex.Length == 6)
            {
                color.r = (byte)System.Convert.ToInt32(hex.Substring(0, 2), 16) / 255f;
                color.g = (byte)System.Convert.ToInt32(hex.Substring(2, 2), 16) / 255f;
                color.b = (byte)System.Convert.ToInt32(hex.Substring(4, 2), 16) / 255f;
                color.a = 1f;
            }
            else if (hex.Length == 8)
            {
                color.r = (byte)System.Convert.ToInt32(hex.Substring(0, 2), 16) / 255f;
                color.g = (byte)System.Convert.ToInt32(hex.Substring(2, 2), 16) / 255f;
                color.b = (byte)System.Convert.ToInt32(hex.Substring(4, 2), 16) / 255f;
                color.a = (byte)System.Convert.ToInt32(hex.Substring(6, 2), 16) / 255f;
            }
            else
            {
                Debug.LogError("Invalid hex color format. Please provide a 6 or 8 character hex color string.");
            }

            return color;
        }

        public static RenderPiplelineType GetCurrentRenderPiplelineType()
        {
            // Assume URP as default
            var renderPipeline = RenderPiplelineType.URP;

            // check if Standard or HDRP
            if (getUsedRenderPipeline() == null)
                renderPipeline = RenderPiplelineType.BuiltIn; // Standard
            else if (!getUsedRenderPipeline().GetType().Name.Contains("Universal"))
                renderPipeline = RenderPiplelineType.HDRP; // HDRP

            return renderPipeline;
        }

        public static Shader GetDefaultShader()
        {
            if (getUsedRenderPipeline() == null)
                return Shader.Find("Standard");
            else
                return getUsedRenderPipeline().defaultShader;
        }

        public static Shader GetDefaultParticleShader()
        {
            if (getUsedRenderPipeline() == null)
                return Shader.Find("Particles/Standard Unlit");
            else
                return getUsedRenderPipeline().defaultParticleMaterial.shader;
        }

        /// <summary>
        /// Returns the current pipline. Returns NULL if it's the standard render pipeline.
        /// </summary>
        /// <returns></returns>
        static UnityEngine.Rendering.RenderPipelineAsset getUsedRenderPipeline()
        {
            if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null)
                return UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            else
                return UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline;
        }

    }
}
#endif
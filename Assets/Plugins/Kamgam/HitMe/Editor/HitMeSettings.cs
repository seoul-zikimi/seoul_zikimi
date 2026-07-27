#if UNITY_EDITOR

#if KAMGAM_VISUAL_SCRIPTING
using Unity.VisualScripting;
#endif
using System.Collections.Generic;
using System.Linq;

using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace Kamgam.HitMe.Settings
{
    // Create a new type of Settings Asset.
    public class HitMeSettings : ScriptableObject
    {
        public enum ShaderVariant { Performance, Gaussian };

        public const string Version = "1.2.8"; 
        public const string SettingsFilePath = "Assets/HitMeSettings.asset";

        [SerializeField, Tooltip(_logLevelTooltip)]
        public Logger.LogLevel LogLevel;
        public const string _logLevelTooltip = "Any log above this log level will not be shown. To turn off all logs choose 'NoLogs'";

        [SerializeField, Tooltip(_showNoDocumentWarningTooltip)]
        public bool ShowNoDocumentWarning;
        public const string _showNoDocumentWarningTooltip = "Should the 'No UIDocument found ..' warning be shown or not?";

        [RuntimeInitializeOnLoadMethod]
        static void bindLoggerLevelToSetting()
        {
            // Notice: This does not yet create a setting instance!
            Logger.OnGetLogLevel = () => GetOrCreateSettings().LogLevel;
        }

        [InitializeOnLoadMethod]
        static void autoCreateSettings()
        {
            GetOrCreateSettings();
        }

        static HitMeSettings cachedSettings;

        public static HitMeSettings GetOrCreateSettings()
        {
            if (cachedSettings == null)
            {
                string typeName = nameof(HitMeSettings);

                cachedSettings = AssetDatabase.LoadAssetAtPath<HitMeSettings>(SettingsFilePath);

                // Still not found? Then search for it.
                if (cachedSettings == null)
                {
                    string[] results = AssetDatabase.FindAssets("t:" + typeName);
                    if (results.Length > 0)
                    {
                        string path = AssetDatabase.GUIDToAssetPath(results[0]);
                        cachedSettings = AssetDatabase.LoadAssetAtPath<HitMeSettings>(path);
                    }
                }

                if (cachedSettings != null)
                {
                    SessionState.EraseBool(typeName + "WaitingForReload");
                }

                // Still not found? Then create settings.
                if (cachedSettings == null)
                {
                    CompilationPipeline.compilationStarted -= onCompilationStarted;
                    CompilationPipeline.compilationStarted += onCompilationStarted;

                    // Are the settings waiting for a recompile to finish? If yes then return null;
                    // This is important if an external script tries to access the settings before they
                    // are deserialized after a re-compile.
                    bool isWaitingForReloadAfterCompilation = SessionState.GetBool(typeName + "WaitingForReload", false);
                    if (isWaitingForReloadAfterCompilation)
                    {
                        Debug.LogWarning(typeName + " is waiting for assembly reload.");
                        return null;
                    }

                    cachedSettings = ScriptableObject.CreateInstance<HitMeSettings>();
                    cachedSettings.LogLevel = Logger.LogLevel.Warning;
                    cachedSettings.ShowNoDocumentWarning = true;

                    AssetDatabase.CreateAsset(cachedSettings, SettingsFilePath);
                    AssetDatabase.SaveAssets();

                    Logger.OnGetLogLevel = () => cachedSettings.LogLevel;

                    MaterialShaderFixer.FixMaterialsDelayed(onSettingsCreated);
                }
            }

            return cachedSettings;
        }

        private static void onCompilationStarted(object obj)
        {
            string typeName = typeof(HitMeSettings).Name;
            SessionState.SetBool(typeName + "WaitingForReload", true);
        }

        // We use this callback instead of CompilationPipeline.compilationFinished because
        // compilationFinished runs before the assembly has been reloaded but DidReloadScripts
        // runs after. And only after we can access the Settings asset.
        [UnityEditor.Callbacks.DidReloadScripts(999000)]
        public static void DidReloadScripts()
        {
            string typeName = typeof(HitMeSettings).Name;
            SessionState.EraseBool(typeName + "WaitingForReload");
        }

        static void onSettingsCreated()
        {
            RebuildNodes();

            bool openManual = EditorUtility.DisplayDialog(
                    "Hit Me",
                    "Thank you for choosing Hit Me.\n\n" +
                    "You'll find the tool under Tools > Hit Me > Open\n\n" +
                    "Please start by reading the manual.\n\n" +
                    "It would be great if you could find the time to leave a review.",
                    "Open manual", "Cancel"
                    );

            if (openManual)
            {
                OpenManual();
            }
        }

        [MenuItem("Tools/Hit Me/Setup Visual Scripting", priority = 1)]
        public static void RebuildNodes()
        {
#if KAMGAM_VISUAL_SCRIPTING
            // If Visual Scripting is not yet initialized then ask the user to do it.
            if (BoltFlow.instance == null || BoltFlow.instance.paths == null)
            {
                EditorUtility.DisplayDialog(
                    "Please initialize Visual Scripting!",
                    "Step 1: Go to Project Settings > Visual Scripting and press the 'Initialize Visual Scripting' button.\n\n" +
                    "Step 2: After initialization go to: Tools > Hit Me > Setup Visual Scripting.",
                    "Okay"
                );
                Debug.LogWarning("Please go to Project Settings > Visual Scripting and press the 'Initialize Visual Scripting' button. Then (that's important) go to: Tools > Hit Me > Setup Visual Scripting.");
            }
            else
            {
                try
                {
                    var assemblyOptionsMetadata = BoltCore.Configuration.GetMetadata(nameof(BoltCore.Configuration.assemblyOptions));
                    var assemblies = (List<LooseAssemblyName>)assemblyOptionsMetadata.value;
                    var typesToAdd = new List<System.Type>() {
                        typeof(ProjectileExtensionsVS),
                        typeof(BallisticProjectile),
                        typeof(BallisticProjectileSource),
                        typeof(BallisticProjectileConfig),
                        typeof(AnimationProjectile),
                        typeof(AnimationProjectileSource),
                        typeof(AnimationProjectileConfig),
                        typeof(ProjectileLineRenderer),
                        typeof(ProjectileRegistry)
                    };
                    bool configModified = false;
                    foreach (var type in typesToAdd)
                    {
                        // Find assembly for reach type and add it (if not yet added)
                        var assembly = System.Reflection.Assembly.GetAssembly(type);
                        int exists = assemblies.Count(asm => asm.name == assembly.GetName().Name);
                        if (exists == 0) 
                        {
                            var looseAssembly = new LooseAssemblyName(assembly.GetName().Name);
                            assemblyOptionsMetadata.Add(looseAssembly);
                            configModified = true;
                        }

                        // Add Type
                        if (!BoltCore.Configuration.typeOptions.Contains(type))
                        {
                            BoltCore.Configuration.typeOptions.Add(type);
                            configModified = true;
                        }
                    }

                    if (configModified)
                    {
                        BoltCore.Configuration.Save();
                        Codebase.UpdateSettings();
                    }

                    // Rebuild the nodes
                    UnitBase.Rebuild();
                }
                catch (System.Exception e)
                {
                    bool openManual = EditorUtility.DisplayDialog("Node setup failed", "Please check the manual on how to set things up manually.", "Open Manual", "Cancel");
                    if (openManual)
                    {
                        OpenManual();
                    }
                    throw e;
                }
            }
#else
            Debug.LogError("Node Build aborted: Please install the Visual Scripting package first!");
#endif
        }

        [MenuItem("Tools/Hit Me/Manual", priority = 101)]
        public static void OpenManual()
        {
            Application.OpenURL("https://kamgam.com/unity/HitMeManual.pdf");
        }

        internal static SerializedObject GetSerializedSettings()
        {
            return new SerializedObject(GetOrCreateSettings());
        }

        [MenuItem("Tools/Hit Me/Settings", priority = 101)]
        public static void OpenSettings()
        {
            var settings = HitMeSettings.GetOrCreateSettings();
            if (settings != null)
            {
                Selection.activeObject = settings;
                EditorGUIUtility.PingObject(settings);
            }
            else
            {
                EditorUtility.DisplayDialog("Error", "Hit Me Settings could not be found or created.", "Ok");
            }
        }

        [MenuItem("Tools/Hit Me/Please leave a review :-)", priority = 410)]
        public static void LeaveReview()
        {
            Application.OpenURL("https://assetstore.unity.com/packages/slug/258386");
        }

        [MenuItem("Tools/Hit Me/More Asset by KAMGAM", priority = 420)]
        public static void MoreAssets()
        {
            Application.OpenURL("https://kamgam.com/unity-assets");
        }

        [MenuItem("Tools/Hit Me/Version: " + Version, priority = 510)]
        public static void LogVersion()
        {
            Debug.Log("Hit Me Version: " + Version + ", Unity: " + Application.unityVersion);
        }

        public void Save()
        {
            EditorUtility.SetDirty(this);
#if UNITY_2021_2_OR_NEWER
            AssetDatabase.SaveAssetIfDirty(this);
#else
            AssetDatabase.SaveAssets();
#endif
        }

    }


#if UNITY_EDITOR
    [CustomEditor(typeof(HitMeSettings))]
    public class HitMeSettingsEditor : Editor
    {
        public HitMeSettings settings;

        public void OnEnable()
        {
            settings = target as HitMeSettings;
        }

        public override void OnInspectorGUI()
        {
            EditorGUILayout.LabelField("Version: " + HitMeSettings.Version);
            base.OnInspectorGUI();
        }
    }
#endif

    static class HitMeSettingsProvider
    {
        [SettingsProvider]
        public static UnityEditor.SettingsProvider CreateHitMeSettingsProvider()
        {
            var provider = new UnityEditor.SettingsProvider("Project/Hit Me", SettingsScope.Project)
            {
                label = "Hit Me",
                guiHandler = (searchContext) =>
                {
                    var settings = HitMeSettings.GetSerializedSettings();

                    var style = new GUIStyle(GUI.skin.label);
                    style.wordWrap = true;

                    EditorGUILayout.LabelField("Version: " + HitMeSettings.Version);
                    if (drawButton(" Open Manual ", icon: "_Help"))
                    {
                        HitMeSettings.OpenManual();
                    }

                    var settingsObj = settings.targetObject as HitMeSettings;

                    drawField("LogLevel", "Log Level", HitMeSettings._logLevelTooltip, settings, style);
                    drawField("ShowNoDocumentWarning", "Show no UI Document warning", HitMeSettings._showNoDocumentWarningTooltip, settings, style);

                    settings.ApplyModifiedProperties();
                },

                // Populate the search keywords to enable smart search filtering and label highlighting.
                keywords = new System.Collections.Generic.HashSet<string>(new[] { "hit", "hitme", "hit me", "projectile", "ballistics", "physics", "shoot" })
            };

            return provider;
        }

        static void drawField(string propertyName, string label, string tooltip, SerializedObject settings, GUIStyle style)
        {
            EditorGUILayout.PropertyField(settings.FindProperty(propertyName), new GUIContent(label));
            if (!string.IsNullOrEmpty(tooltip))
            {
                GUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.Label(tooltip, style);
                GUILayout.EndVertical();
            }
            GUILayout.Space(10);
        }

        static bool drawButton(string text, string tooltip = null, string icon = null, params GUILayoutOption[] options)
        {
            GUIContent content;

            // icon
            if (!string.IsNullOrEmpty(icon))
                content = EditorGUIUtility.IconContent(icon);
            else
                content = new GUIContent();

            // text
            content.text = text;

            // tooltip
            if (!string.IsNullOrEmpty(tooltip))
                content.tooltip = tooltip;

            return GUILayout.Button(content, options);
        }
    }
}
#endif
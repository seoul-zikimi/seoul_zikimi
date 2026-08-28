using System;
using System.Reflection;
using UnityEngine;

namespace GridSystem
{
    /// <summary>
    /// GridSystem assembly에서 Assembly-CSharp의 SoundManager/SFXType/GamePhase를 직접 참조할 수 없어
    /// 문자열 기반 reflection으로 안전하게 호출하는 얇은 브릿지.
    /// SoundManager가 없거나 enum 이름이 없으면 조용히 무시한다.
    /// </summary>
    internal static class GridSoundBridge
    {
        private static Type s_SoundManagerType;
        private static Type s_SfxType;
        private static Type s_GamePhaseType;
        private static PropertyInfo s_InstanceProperty;
        private static MethodInfo s_PlaySfxMethod;
        private static MethodInfo s_PlaySfxAtMethod;
        private static MethodInfo s_SetPhaseMethod;
        private static MethodInfo s_PlayBgmMethod;

        public static void PlaySFX(string sfxName)
        {
            if (!TryGetInstance(out var instance) || !TryParseEnum(SfxType, sfxName, out var value))
                return;

            s_PlaySfxMethod ??= SoundManagerType.GetMethod("PlaySFX", new[] { SfxType });
            s_PlaySfxMethod?.Invoke(instance, new[] { value });
        }

        public static void PlaySFXAt(string sfxName, Vector3 worldPos)
        {
            if (!TryGetInstance(out var instance) || !TryParseEnum(SfxType, sfxName, out var value))
                return;

            s_PlaySfxAtMethod ??= SoundManagerType.GetMethod("PlaySFXAt", new[] { SfxType, typeof(Vector3) });
            s_PlaySfxAtMethod?.Invoke(instance, new[] { value, worldPos });
        }

        public static void SetPhase(string phaseName)
        {
            if (!TryGetInstance(out var instance) || !TryParseEnum(GamePhaseType, phaseName, out var value))
                return;

            s_SetPhaseMethod ??= SoundManagerType.GetMethod("SetPhase", new[] { GamePhaseType });
            s_SetPhaseMethod?.Invoke(instance, new[] { value });
        }

        /// <summary>맵 전용 BGM(SoundLibrary 미등록 곡)으로 crossfade. 같은 곡이면 SoundManager가 무시한다.</summary>
        public static void PlayBGM(AudioClip clip)
        {
            if (clip == null || !TryGetInstance(out var instance))
                return;

            s_PlayBgmMethod ??= SoundManagerType.GetMethod("PlayBGM", new[] { typeof(AudioClip) });
            s_PlayBgmMethod?.Invoke(instance, new object[] { clip });
        }

        /// <summary>페이즈 BGM을 걸되, 맵 카드(MapDef)에 그 페이즈용 전용 곡이 있으면 그걸 우선한다.
        /// 맵별 BGM의 유일한 진입점 — GameLoopManager는 SetPhase 대신 이걸 부른다.</summary>
        public static void SetPhaseForMap(string phaseName, int mapIndex)
        {
            var catalog = MapCatalog.Instance;
            var def = catalog != null ? catalog.Get(mapIndex) : null;
            var mapClip = def != null ? def.Bgm.For(phaseName) : null;

            if (mapClip != null) PlayBGM(mapClip);
            else SetPhase(phaseName);
        }

        private static Type SoundManagerType => s_SoundManagerType ??= Type.GetType("SoundManager, Assembly-CSharp");
        private static Type SfxType => s_SfxType ??= Type.GetType("SFXType, Assembly-CSharp");
        private static Type GamePhaseType => s_GamePhaseType ??= Type.GetType("GamePhase, Assembly-CSharp");

        private static bool TryGetInstance(out object instance)
        {
            instance = null;
            if (SoundManagerType == null)
                return false;

            s_InstanceProperty ??= FindStaticInstanceProperty(SoundManagerType);
            instance = s_InstanceProperty?.GetValue(null);
            return instance != null;
        }

        /// <summary>
        /// static 프로퍼티 Instance 를 상속 계층을 거슬러 올라가며 찾는다.
        /// SoundManager 는 Instance 를 직접 선언하지 않고 Singleton&lt;SoundManager&gt; 에서 물려받는데,
        /// BindingFlags 를 명시한 GetProperty 는 상속된 static 멤버를 돌려주지 않는다(FlattenHierarchy 필요).
        /// 게다가 제네릭 베이스의 static 멤버는 FlattenHierarchy 로도 누락되는 경우가 있어,
        /// 각 단계를 DeclaredOnly 로 직접 조회한다 — 실제로 선언된 Singleton&lt;SoundManager&gt; 에서 잡힌다.
        /// 이 조회가 실패하면 브릿지 전체가 무음이 되므로 조용히 넘기지 않고 한 번 경고한다.
        /// </summary>
        private static PropertyInfo FindStaticInstanceProperty(Type type)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly;
            for (var cur = type; cur != null; cur = cur.BaseType)
            {
                var prop = cur.GetProperty("Instance", flags);
                if (prop != null) return prop;
            }

            if (!s_WarnedNoInstance)
            {
                s_WarnedNoInstance = true;
                Debug.LogWarning($"[GridSoundBridge] {type.Name}에서 static Instance 프로퍼티를 찾지 못했습니다 — " +
                                 "이 어셈블리에서 나가는 효과음·BGM이 전부 무음이 됩니다.");
            }
            return null;
        }

        private static bool s_WarnedNoInstance;

        private static bool TryParseEnum(Type enumType, string enumName, out object value)
        {
            value = null;
            if (enumType == null || string.IsNullOrEmpty(enumName))
                return false;

            try
            {
                value = Enum.Parse(enumType, enumName);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}

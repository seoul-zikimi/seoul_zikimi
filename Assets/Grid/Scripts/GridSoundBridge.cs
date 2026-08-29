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

        // 호출당 할당 제거: 파싱된 enum 박싱 값 캐시(실패도 null로 캐시해 반복 예외 방지) + Invoke 인자 버퍼 재사용.
        // 효과음은 연타 중 초당 수 회 이 브릿지를 타므로 매 호출 Enum.Parse + object[] 할당이 핫패스였다.
        private static readonly System.Collections.Generic.Dictionary<string, object> s_SfxValueCache = new();
        private static readonly System.Collections.Generic.Dictionary<string, object> s_PhaseValueCache = new();
        private static readonly object[] s_Args1 = new object[1];
        private static readonly object[] s_Args2 = new object[2];

        public static void PlaySFX(string sfxName)
        {
            if (!TryGetInstance(out var instance) || !TryParseEnum(SfxType, sfxName, s_SfxValueCache, out var value))
                return;

            s_PlaySfxMethod ??= SoundManagerType.GetMethod("PlaySFX", new[] { SfxType });
            s_Args1[0] = value;
            s_PlaySfxMethod?.Invoke(instance, s_Args1);
        }

        public static void PlaySFXAt(string sfxName, Vector3 worldPos)
        {
            if (!TryGetInstance(out var instance) || !TryParseEnum(SfxType, sfxName, s_SfxValueCache, out var value))
                return;

            s_PlaySfxAtMethod ??= SoundManagerType.GetMethod("PlaySFXAt", new[] { SfxType, typeof(Vector3) });
            s_Args2[0] = value; s_Args2[1] = worldPos;
            s_PlaySfxAtMethod?.Invoke(instance, s_Args2);
        }

        public static void SetPhase(string phaseName)
        {
            if (!TryGetInstance(out var instance) || !TryParseEnum(GamePhaseType, phaseName, s_PhaseValueCache, out var value))
                return;

            s_SetPhaseMethod ??= SoundManagerType.GetMethod("SetPhase", new[] { GamePhaseType });
            s_Args1[0] = value;
            s_SetPhaseMethod?.Invoke(instance, s_Args1);
        }

        /// <summary>맵 전용 BGM(SoundLibrary 미등록 곡)으로 crossfade. 같은 곡이면 SoundManager가 무시한다.</summary>
        public static void PlayBGM(AudioClip clip)
        {
            if (clip == null || !TryGetInstance(out var instance))
                return;

            s_PlayBgmMethod ??= SoundManagerType.GetMethod("PlayBGM", new[] { typeof(AudioClip) });
            s_Args1[0] = clip;
            s_PlayBgmMethod?.Invoke(instance, s_Args1);
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

        private static bool TryParseEnum(Type enumType, string enumName,
                                         System.Collections.Generic.Dictionary<string, object> cache, out object value)
        {
            value = null;
            if (enumType == null || string.IsNullOrEmpty(enumName))
                return false;

            if (cache.TryGetValue(enumName, out value))
                return value != null;   // 이름 오타 등 실패도 null로 캐시 — 매 호출 예외 반복 방지

            try
            {
                value = Enum.Parse(enumType, enumName);
            }
            catch
            {
                value = null;
            }
            cache[enumName] = value;
            return value != null;
        }
    }
}

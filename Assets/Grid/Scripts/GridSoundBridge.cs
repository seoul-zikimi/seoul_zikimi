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

        private static Type SoundManagerType => s_SoundManagerType ??= Type.GetType("SoundManager, Assembly-CSharp");
        private static Type SfxType => s_SfxType ??= Type.GetType("SFXType, Assembly-CSharp");
        private static Type GamePhaseType => s_GamePhaseType ??= Type.GetType("GamePhase, Assembly-CSharp");

        private static bool TryGetInstance(out object instance)
        {
            instance = null;
            if (SoundManagerType == null)
                return false;

            s_InstanceProperty ??= SoundManagerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            instance = s_InstanceProperty?.GetValue(null);
            return instance != null;
        }

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

using System;
using UnityEngine;

namespace SeoulZikimi.Weather
{
    /// <summary>낮/밤에 맞는 스카이박스를 RenderSettings에 적용한다.</summary>
    public sealed class UnityTimeOfDaySkyboxPresenter :
        MonoBehaviour,
        ITimeOfDaySkyboxPresenter
    {
        [SerializeField] private Material m_DaySkybox = null;
        [SerializeField] private Material m_NightSkybox = null;

        private Material _originalSkybox;
        private bool _hasCapturedOriginal;

        public void ApplySkybox(TimeOfDayVisualProfile profile)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));

            CaptureOriginal();
            Material target = profile.TimeOfDay == TimeOfDay.Day
                ? m_DaySkybox
                : m_NightSkybox;

            if (target == null)
            {
                Debug.LogWarning(
                    $"[{nameof(UnityTimeOfDaySkyboxPresenter)}] " +
                    $"{profile.SkyboxVariantKey} 스카이박스가 지정되지 않았습니다.",
                    this);
                return;
            }

            Apply(target);
        }

        public void ResetSkybox()
        {
            if (_hasCapturedOriginal)
                Apply(_originalSkybox);
        }

        private void CaptureOriginal()
        {
            if (_hasCapturedOriginal)
                return;

            _originalSkybox = RenderSettings.skybox;
            _hasCapturedOriginal = true;
        }

        private static void Apply(Material skybox)
        {
            RenderSettings.skybox = skybox;
            DynamicGI.UpdateEnvironment();
        }
    }
}

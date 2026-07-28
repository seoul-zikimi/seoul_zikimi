using System;
using System.Collections;
using UnityEngine;

namespace SeoulZikimi.Weather
{
    /// <summary>
    /// 등록된 조명들의 원래 밝기를 기준으로 낮/밤 밝기를 부드럽게 전환한다.
    /// 스크립트만 제공하며 현재 씬의 오브젝트에는 자동으로 부착되지 않는다.
    /// </summary>
    public sealed class UnityTimeOfDayLightingPresenter :
        MonoBehaviour,
        ITimeOfDayLightingPresenter
    {
        [SerializeField] private Light[] m_Lights = Array.Empty<Light>();

        private float[] _baseIntensities;
        private Coroutine _transition;
        private bool _hasCapturedBaseline;

        public void ApplyLighting(TimeOfDayVisualProfile profile)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));

            CaptureBaseline();
            StartTransition(profile.LightIntensityMultiplier, profile.TransitionDuration);
        }

        public void ResetLighting()
        {
            if (!_hasCapturedBaseline)
                return;

            StopActiveTransition();
            SetIntensityMultiplier(1f);
        }

        private void CaptureBaseline()
        {
            if (_hasCapturedBaseline)
                return;

            _baseIntensities = new float[m_Lights.Length];
            for (int i = 0; i < m_Lights.Length; i++)
            {
                if (m_Lights[i] != null)
                    _baseIntensities[i] = m_Lights[i].intensity;
            }

            _hasCapturedBaseline = true;
        }

        private void StartTransition(float targetMultiplier, float duration)
        {
            StopActiveTransition();

            if (!isActiveAndEnabled || duration <= 0f)
            {
                SetIntensityMultiplier(targetMultiplier);
                return;
            }

            _transition = StartCoroutine(TransitionRoutine(targetMultiplier, duration));
        }

        private IEnumerator TransitionRoutine(float targetMultiplier, float duration)
        {
            float[] startIntensities = new float[m_Lights.Length];
            for (int i = 0; i < m_Lights.Length; i++)
            {
                if (m_Lights[i] != null)
                    startIntensities[i] = m_Lights[i].intensity;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = Mathf.Clamp01(elapsed / duration);

                for (int i = 0; i < m_Lights.Length; i++)
                {
                    if (m_Lights[i] != null)
                    {
                        float target = _baseIntensities[i] * targetMultiplier;
                        m_Lights[i].intensity = Mathf.Lerp(startIntensities[i], target, progress);
                    }
                }

                yield return null;
            }

            SetIntensityMultiplier(targetMultiplier);
            _transition = null;
        }

        private void SetIntensityMultiplier(float multiplier)
        {
            for (int i = 0; i < m_Lights.Length; i++)
            {
                if (m_Lights[i] != null)
                    m_Lights[i].intensity = _baseIntensities[i] * multiplier;
            }
        }

        private void StopActiveTransition()
        {
            if (_transition == null)
                return;

            StopCoroutine(_transition);
            _transition = null;
        }
    }
}

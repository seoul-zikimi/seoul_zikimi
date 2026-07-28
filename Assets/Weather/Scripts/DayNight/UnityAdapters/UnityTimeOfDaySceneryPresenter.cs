using System;
using UnityEngine;

namespace SeoulZikimi.Weather
{
    /// <summary>
    /// 맵의 낮 전경 그룹과 밤 전경 그룹을 전환한다.
    /// 건물 창문 불빛, 간판, 원경 모델 등을 각 배열에 나눠 등록할 수 있다.
    /// </summary>
    public sealed class UnityTimeOfDaySceneryPresenter :
        MonoBehaviour,
        ITimeOfDaySceneryPresenter
    {
        [SerializeField] private GameObject[] m_DayScenery = Array.Empty<GameObject>();
        [SerializeField] private GameObject[] m_NightScenery = Array.Empty<GameObject>();

        private bool[] _originalDayStates;
        private bool[] _originalNightStates;
        private bool _hasCapturedOriginal;

        public void ApplyScenery(TimeOfDayVisualProfile profile)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));

            CaptureOriginalStates();
            bool isDay = profile.TimeOfDay == TimeOfDay.Day;
            SetGroupActive(m_DayScenery, isDay);
            SetGroupActive(m_NightScenery, !isDay);
        }

        public void ResetScenery()
        {
            if (!_hasCapturedOriginal)
                return;

            RestoreGroupStates(m_DayScenery, _originalDayStates);
            RestoreGroupStates(m_NightScenery, _originalNightStates);
        }

        private void CaptureOriginalStates()
        {
            if (_hasCapturedOriginal)
                return;

            _originalDayStates = CaptureGroupStates(m_DayScenery);
            _originalNightStates = CaptureGroupStates(m_NightScenery);
            _hasCapturedOriginal = true;
        }

        private static bool[] CaptureGroupStates(GameObject[] group)
        {
            var states = new bool[group.Length];
            for (int i = 0; i < group.Length; i++)
            {
                if (group[i] != null)
                    states[i] = group[i].activeSelf;
            }

            return states;
        }

        private static void SetGroupActive(GameObject[] group, bool isActive)
        {
            for (int i = 0; i < group.Length; i++)
            {
                if (group[i] != null)
                    group[i].SetActive(isActive);
            }
        }

        private static void RestoreGroupStates(GameObject[] group, bool[] states)
        {
            for (int i = 0; i < group.Length; i++)
            {
                if (group[i] != null)
                    group[i].SetActive(states[i]);
            }
        }
    }
}

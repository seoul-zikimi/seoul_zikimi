using TMPro;
using UnityEngine;

/// <summary>빌드에서도 동일한 UI 폰트를 직접 참조하기 위한 Resources 설정.</summary>
public sealed class JobsnailFontSet : ScriptableObject
{
    [SerializeField] private Font m_LegacyFont;
    [SerializeField] private TMP_FontAsset m_TmpFont;

    public Font LegacyFont => m_LegacyFont;
    public TMP_FontAsset TmpFont => m_TmpFont;
}

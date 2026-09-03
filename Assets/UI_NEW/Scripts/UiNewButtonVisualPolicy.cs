using UnityEngine;
using UnityEngine.UI;

namespace SeoulZikimi.UI.New
{
    /// <summary>UI_NEW에서는 버튼 크기를 움직이지 않고 색상으로만 상호작용을 표시한다.</summary>
    internal static class UiNewButtonVisualPolicy
    {
        private static readonly Color Highlight = new(0.90f, 0.95f, 1f, 1f);
        private static readonly Color Pressed = new(0.80f, 0.89f, 0.98f, 1f);

        public static void Apply(Transform root)
        {
            if (root == null) return;

            JobsnailUiKit.ApplyFontPolicy(root);

            foreach (JuicyButton motion in root.GetComponentsInChildren<JuicyButton>(true))
            {
                motion.enabled = false;
                motion.transform.localScale = Vector3.one;
                // UI_NEW에는 크기 모션이 다시 켜질 여지를 남기지 않는다.
                Object.Destroy(motion);
            }

            foreach (Button button in root.GetComponentsInChildren<Button>(true))
            {
                button.transition = Selectable.Transition.ColorTint;
                ColorBlock colors = button.colors;
                if (button.name.StartsWith("Option_"))
                {
                    // 드롭다운 옵션 행: 이미지 = 하늘색 강조박스. 평소엔 투명, 커서를 올렸을 때만 보인다
                    // (피그마 '드롭박스 - 펼쳐졌을 때 커서 올린 곳 강조박스'). 선택 후에도 남지 않게 selected도 투명.
                    colors.normalColor = new Color(1f, 1f, 1f, 0f);
                    colors.highlightedColor = Color.white;
                    colors.pressedColor = new Color(0.88f, 0.94f, 1f, 1f);
                    colors.selectedColor = new Color(1f, 1f, 1f, 0f);
                }
                else
                {
                    colors.normalColor = Color.white;
                    colors.highlightedColor = Highlight;
                    colors.pressedColor = Pressed;
                    colors.selectedColor = Highlight;
                }
                colors.colorMultiplier = 1f;
                colors.fadeDuration = 0.05f;
                button.colors = colors;
            }
        }
    }
}

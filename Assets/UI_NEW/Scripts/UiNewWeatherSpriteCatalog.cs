using SeoulZikimi.Weather;
using UnityEngine;

namespace SeoulZikimi.UI.New
{
    /// <summary>UI_NEW 날씨 아틀라스의 하위 Sprite를 WeatherKind로 조회한다.</summary>
    public static class UiNewWeatherSpriteCatalog
    {
        public static Sprite Get(WeatherKind weather)
        {
            return Resources.Load<Sprite>($"UI_NEW/Weather/UI/{weather}");
        }
    }
}

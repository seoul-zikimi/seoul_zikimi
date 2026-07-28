namespace SeoulZikimi.Weather
{
    public interface ITimeOfDaySelector
    {
        TimeOfDaySelection Select(DayNightSessionOptions options);
    }

    public interface ITimeOfDayProfileCatalog
    {
        TimeOfDayVisualProfile GetProfile(TimeOfDay timeOfDay);
    }

    /// <summary>Directional Light, 환경광 등 조명만 담당한다.</summary>
    public interface ITimeOfDayLightingPresenter
    {
        void ApplyLighting(TimeOfDayVisualProfile profile);
        void ResetLighting();
    }

    /// <summary>낮/밤 스카이박스 교체 또는 블렌딩만 담당한다.</summary>
    public interface ITimeOfDaySkyboxPresenter
    {
        void ApplySkybox(TimeOfDayVisualProfile profile);
        void ResetSkybox();
    }

    /// <summary>
    /// 맵별 낮/밤 전경 오브젝트 활성화나 머티리얼 교체만 담당한다.
    /// 맵마다 별도 구현체를 둘 수 있다.
    /// </summary>
    public interface ITimeOfDaySceneryPresenter
    {
        void ApplyScenery(TimeOfDayVisualProfile profile);
        void ResetScenery();
    }
}

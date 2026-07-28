using System;

namespace SeoulZikimi.Weather
{
    public static class DefaultDayNightFactory
    {
        public static DayNightController Create(
            ITimeOfDayLightingPresenter lighting,
            ITimeOfDaySkyboxPresenter skybox,
            ITimeOfDaySceneryPresenter scenery,
            IRandomSource random = null,
            ITimeOfDayProfileCatalog profiles = null)
        {
            if (lighting == null)
                throw new ArgumentNullException(nameof(lighting));
            if (skybox == null)
                throw new ArgumentNullException(nameof(skybox));
            if (scenery == null)
                throw new ArgumentNullException(nameof(scenery));

            random ??= new SystemRandomSource();
            profiles ??= TimeOfDayProfileCatalog.CreateDefault();

            return new DayNightController(
                new RandomTimeOfDaySelector(random),
                profiles,
                lighting,
                skybox,
                scenery);
        }
    }
}

#if KAMGAM_VISUAL_SCRIPTING
using Unity.VisualScripting;
using UnityEngine;

namespace Kamgam.HitMe.Nodes
{
    public static partial class EventNames
    {
        public static string OnBallisticStart = "HitMe_EventBallisticStart";
    }

    [IncludeInSettings(include: false)]
    [UnitCategory("Events\\Hit Me")] // Events have to be under the Events category: https://forum.unity.com/threads/c-custom-unitcategory-for-events.1178791/#post-7549207
    [UnitTitle("Ballistic Projectile Start Event")]
    public class BallisticProjectileStartEvent : ProjectileEventNodeBase<BallisticProjectile, Collision, Collider>
    {
        protected override string getHookName()
        {
            return EventNames.OnBallisticStart;
        }

        protected override void registerEvent(BallisticProjectile projectile)
        {
            projectile.OnStart += onStart;
        }

        protected override void unregisterEvent(BallisticProjectile projectile)
        {
            projectile.OnStart -= onStart;
        }

        public void onStart(BallisticProjectile projectile)
        {
            triggerEvent(projectile);
        }
    }
}
#endif
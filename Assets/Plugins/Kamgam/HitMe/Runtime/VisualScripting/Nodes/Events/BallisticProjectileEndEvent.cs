#if KAMGAM_VISUAL_SCRIPTING
using Unity.VisualScripting;
using UnityEngine;

namespace Kamgam.HitMe.Nodes
{
    public static partial class EventNames
    {
        public static string OnBallisticEnd = "HitMe_EventBallisticEnd";
    }

    [IncludeInSettings(include: false)]
    [UnitCategory("Events\\Hit Me")] // Events have to be under the Events category: https://forum.unity.com/threads/c-custom-unitcategory-for-events.1178791/#post-7549207
    [UnitTitle("Ballistic Projectile End Event")]
    public class BallisticProjectileEndEvent : ProjectileEventNodeBase<BallisticProjectile, Collision, Collider>
    {
        protected override string getHookName()
        {
            return EventNames.OnBallisticEnd;
        }

        protected override void registerEvent(BallisticProjectile projectile)
        {
            projectile.OnEnd += onEnd;
        }

        protected override void unregisterEvent(BallisticProjectile projectile)
        {
            projectile.OnEnd -= onEnd;
        }

        public void onEnd(BallisticProjectile projectile)
        {
            triggerEvent(projectile);
        }
    }
}
#endif
#if KAMGAM_VISUAL_SCRIPTING
using Unity.VisualScripting;
using UnityEngine;

namespace Kamgam.HitMe.Nodes
{
    public static partial class EventNames
    {
        public static string OnBallisticUpdate = "HitMe_EventBallisticUpdate";
    }

    [IncludeInSettings(include: false)]
    [UnitCategory("Events\\Hit Me")] // Events have to be under the Events category: https://forum.unity.com/threads/c-custom-unitcategory-for-events.1178791/#post-7549207
    [UnitTitle("Ballistic Projectile Update Event")]
    public class BallisticProjectileUpdateEvent : ProjectileEventNodeBase<BallisticProjectile, Collision, Collider>
    {
        protected override string getHookName()
        {
            return EventNames.OnBallisticUpdate;
        }

        protected override void registerEvent(BallisticProjectile projectile)
        {
            projectile.OnUpdate += onUpdate;
        }

        protected override void unregisterEvent(BallisticProjectile projectile)
        {
            projectile.OnUpdate -= onUpdate;
        }

        public void onUpdate(BallisticProjectile projectile)
        {
            triggerEvent(projectile);
        }
    }
}
#endif
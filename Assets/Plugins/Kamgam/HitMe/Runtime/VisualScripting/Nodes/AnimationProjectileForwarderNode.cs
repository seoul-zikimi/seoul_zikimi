#if KAMGAM_VISUAL_SCRIPTING
using Unity.VisualScripting;

namespace Kamgam.HitMe.Nodes
{
    [IncludeInSettings(include: false)]
    [UnitTitle("Animation Projectile Forwarder")]
    [UnitCategory("HitMe")]
    public class AnimationProjectileForwarderNode : Unit
    {
        public ValueInput ProjectileIn;
        public ValueOutput ProjectileOut;

        protected override void Definition()
        {
            ProjectileOut = ValueOutput("Projectile", (flow) => flow.GetValue<AnimationProjectile>(ProjectileIn) );
            ProjectileIn = ValueInput<AnimationProjectile>("Projectile");
        }
    }
}
#endif
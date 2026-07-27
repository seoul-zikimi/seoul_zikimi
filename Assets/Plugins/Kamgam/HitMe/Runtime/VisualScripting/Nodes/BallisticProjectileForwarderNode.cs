#if KAMGAM_VISUAL_SCRIPTING
using Unity.VisualScripting;

namespace Kamgam.HitMe.Nodes
{
    [IncludeInSettings(include: false)]
    [UnitTitle("Ballistic Projectile Forwarder")]
    [UnitCategory("HitMe")]
    public class BallisticProjectileForwarderNode : Unit
    {
        public ValueInput ProjectileIn;
        public ValueOutput ProjectileOut;

        protected override void Definition()
        {
            ProjectileOut = ValueOutput("Projectile", (flow) => flow.GetValue<BallisticProjectile>(ProjectileIn) );
            ProjectileIn = ValueInput<BallisticProjectile>("Projectile");
        }
    }
}
#endif
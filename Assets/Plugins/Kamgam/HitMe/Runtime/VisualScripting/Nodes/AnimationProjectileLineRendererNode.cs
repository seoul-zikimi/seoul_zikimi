#if KAMGAM_VISUAL_SCRIPTING
using Unity.VisualScripting;
using UnityEngine;

namespace Kamgam.HitMe.Nodes
{
    [IncludeInSettings(include: false)]
    [UnitTitle("Animation Projectile Line Renderer")]
    [UnitCategory("HitMe")]
    [TypeIcon(typeof(LineRenderer))]
    public class AnimationProjectileLineRendererNode : AnimationProjectileLineRendererNodeBase<ProjectileLineRenderer>
    {
    }
}
#endif
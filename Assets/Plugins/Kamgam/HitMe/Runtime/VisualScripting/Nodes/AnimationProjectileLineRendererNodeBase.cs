#if KAMGAM_VISUAL_SCRIPTING
using Unity.VisualScripting;
using UnityEngine;

namespace Kamgam.HitMe.Nodes
{
    public class AnimationProjectileLineRendererNodeBase<TRenderer> : Unit
        where TRenderer : MonoBehaviour, IProjectileLineRenderer
    {
        public ControlInput Draw;
        public ControlOutput Drawn;

        /// <summary>
        /// Specify either Config or Source. It is recommended that if Source is available 
        /// to use Source instead of Config to catch the transform override on Source.
        /// </summary>
        public ValueInput Source;

        /// <summary>
        /// Specify either Config or Source. It is recommended that if Source is available 
        /// to use Source instead of Config to catch the transform override on Source.
        /// </summary>
        public ValueInput Config;

        /// <summary>
        /// The prefab used for instaniating a new renderer if needed.
        /// </summary>
        public ValueInput RendererPrefab;

        /// <summary>
        /// If null then a new renderer instance will be created and return at the Renderer output.
        /// </summary>
        public ValueInput CachedRenderer;

        public ValueInput SegmentsPerUnit;
        public ValueInput UsePrediction;

        public ValueOutput Renderer;

        protected GameObject _lastPrefab;
        protected TRenderer _renderer;
        protected bool _possible;

        protected override void Definition()
        {
            Draw = ControlInput(nameof(Draw), DrawRenderer);
            Drawn = ControlOutput(nameof(Drawn));

            Source = ValueInput<AnimationProjectileSource>("Projectile Source", null).AllowsNull();
            Config = ValueInput<AnimationProjectileConfig>("Animation Config", null).AllowsNull();
            RendererPrefab = ValueInput<GameObject>("Renderer Prefab", null);
            CachedRenderer = ValueInput<TRenderer>("Cached Renderer", null);
            SegmentsPerUnit = ValueInput<float>(nameof(SegmentsPerUnit), 1f);
            UsePrediction = ValueInput<bool>(nameof(UsePrediction), true);
            Renderer = ValueOutput("Renderer", (flow) => { return _renderer; });

            Succession(Draw, Drawn);
        }

        protected ControlOutput DrawRenderer(Flow flow)
        {
            GameObject prefab = flow.GetValue<GameObject>(RendererPrefab);

            // Clear cache renderer if prefab changed.
            _renderer = flow.GetValue<TRenderer>(CachedRenderer);

            AnimationProjectileConfig config;
            var projectileSource = flow.GetValue<AnimationProjectileSource>(Source);
            // The config is not recognized by VisualScripting as a type that can have a default value, see: 
            // See: https://forum.unity.com/threads/unable-to-provide-a-default-for-getvalue-on-object-valueinput.1140022/
            // The workaround is that since config and projectileSource are EITHER-OR we use the presence of projectileSource
            // as a flog on whether or not we call config.
            if (projectileSource != null)
                config = projectileSource.Config;
            else
                config = flow.GetValue<AnimationProjectileConfig>(Config);

            float segments = flow.GetValue<float>(SegmentsPerUnit);
            bool usePrediction = flow.GetValue<bool>(UsePrediction);

            var source = AnimationProjectileConfig.ResolveSource(config, projectileSource != null ? projectileSource.SourceOverride : null);
            var target = AnimationProjectileConfig.ResolveTarget(config, projectileSource != null ? projectileSource.TargetOverride : null);

            _renderer = AnimationProjectile.DrawPathGeneric<TRenderer>(source, target, config, segments, _renderer, prefab, usePrediction);

            return Drawn;
        }
    }
}
#endif
#if KAMGAM_VISUAL_SCRIPTING
using Unity.VisualScripting;
using UnityEngine;

namespace Kamgam.HitMe.Nodes
{
    public abstract class ProjectileEventNodeBase<TProjectile, TCollision, TCollider> : EventUnit<ProjectileEventData<TProjectile, TCollision, TCollider>> 
        where TProjectile : IProjectile  
        where TCollision : class
        where TCollider : class
    {
        [DoNotSerialize]
        public ControlInput startListening;

        [DoNotSerialize]
        public ControlInput stopListening;

        [DoNotSerialize]
        public ControlOutput started;

        [DoNotSerialize]
        public ControlOutput stopped;

        [DoNotSerialize]
        public ValueInput projectileIn;

        [DoNotSerialize]
        public ValueOutput projectileOut;

        protected override bool register => true;

        protected abstract string getHookName();

        protected string _uniqueHookName;

        protected string getUniqueHookName()
        {
            if(string.IsNullOrEmpty(_uniqueHookName))
                _uniqueHookName = getHookName() + guid.ToString(); ;

            return _uniqueHookName;
        }

        public override EventHook GetHook(GraphReference reference)
        {
            return new EventHook(getUniqueHookName());
        }

        protected override void Definition()
        {

            startListening = ControlInput(nameof(startListening), startListeningAction);
            started = ControlOutput(nameof(started));

            stopListening = ControlInput(nameof(stopListening), stopListeningAction);
            stopped = ControlOutput(nameof(stopped));

            base.Definition();

            projectileIn = ValueInput<GameObject>("Projectile", null);
            projectileOut = ValueOutput<TProjectile>("Projectile", (flow) => flow.GetValue<TProjectile>(projectileIn));

            definition();

            Succession(startListening, started);
            Succession(stopListening, stopped);
        }

        protected virtual void definition() { }

        protected virtual ControlOutput startListeningAction(Flow flow)
        {
            var projectile = flow.GetValue<TProjectile>(projectileIn);
            if (projectile != null)
            {
                unregisterEvent(projectile);
                registerEvent(projectile);
            }

            return started;
        }

        protected virtual ControlOutput stopListeningAction(Flow flow)
        {
            var projectile = flow.GetValue<TProjectile>(projectileIn);
            if (projectile != null)
            {
                unregisterEvent(projectile);
            }

            return stopped;
        }

        public override void StartListening(GraphStack stack)
        {
            base.StartListening(stack);

            var projectile = Flow.FetchValue<TProjectile>(projectileIn, stack.ToReference());
            if (projectile != null)
            {
                unregisterEvent(projectile);
                registerEvent(projectile);

            }
        }

        protected virtual void triggerEvent(TProjectile projectile)
        {
            var data = new ProjectileEventData<TProjectile, TCollision, TCollider>(projectile, null, null, null);
            EventBus.Trigger(getUniqueHookName(), data);
        }

        protected virtual void triggerEvent(TProjectile projectile, TCollision collision, CollisionTrigger trigger, TCollider collider)
        {
            var data = new ProjectileEventData<TProjectile, TCollision, TCollider>(projectile, collision, trigger, collider);
            EventBus.Trigger(getUniqueHookName(), data);
        }

        protected override void AssignArguments(Flow flow, ProjectileEventData<TProjectile, TCollision, TCollider> data)
        {
            flow.SetValue(projectileOut, data.Projectile);
            assignArguments(flow, data.Projectile, data.Collision, data.Trigger, data.Collider);
        }

        protected abstract void registerEvent(TProjectile projectile);
        protected abstract void unregisterEvent(TProjectile projectile);
        protected virtual void assignArguments(Flow flow, TProjectile projectile, TCollision collision, CollisionTrigger trigger, TCollider collider) { }
    }
}
#endif
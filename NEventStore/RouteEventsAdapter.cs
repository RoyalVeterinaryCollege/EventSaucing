using System;
using CommonDomain;

namespace EventSaucing.NEventStore {
    /// <summary>
    /// Adapts SharedConventionEventRouter to the interface that NEventStore requires
    /// </summary>
    public class RouteEventsAdapter : IRouteEvents {
        private readonly SharedConventionEventRouter _conventionRouter;
        private readonly IAggregate _aggregate;

        public RouteEventsAdapter(SharedConventionEventRouter conventionRouter, IAggregate aggregate) {
            _conventionRouter = conventionRouter;
            _aggregate = aggregate;
        }

        public void Dispatch(object eventMessage) {
            _conventionRouter.Dispatch(_aggregate, eventMessage);
        }

        public void Register(IAggregate aggregate) {
            //no-op
        }

        public void Register<T>(Action<T> handler) {
            //no-op
        }
    }
}
using System;
using System.Collections.Concurrent;
using NEventStore.Domain;
using Scalesque;

namespace EventSaucing.NEventStore {

    /// <summary>
    /// Thread safe router for aggregate event application.  Holds a singleton SharedConventionEventRouter for each aggregate type to avoid reflecting on each instance.
    /// </summary>
    public interface ISharedEventApplicationRoutes {
        /// <summary>
        /// Gets an IRouteEvents for a given aggregate
        /// </summary>
        /// <param name="aggregate"></param>
        /// <returns></returns>
        IRouteEvents GetRoutesFor(IAggregate aggregate);
    }

    /// <summary>
    /// Thread safe router for aggregate event application.  Holds a singleton SharedConventionEventRouter for each aggregate type to avoid reflecting on each instance.
    /// </summary>
    public class SharedEventApplicationRoutes : ISharedEventApplicationRoutes {
        /// <summary>
        /// typeof(IAggregate) -> event router for that aggregate type
        /// </summary>
        private ConcurrentDictionary<Type, SharedConventionEventRouter> routes = new ConcurrentDictionary<Type, SharedConventionEventRouter>();

        /// <summary>
        /// Gets an IRouteEvents for a given aggregate
        /// </summary>
        /// <param name="aggregate"></param>
        /// <returns></returns>
        public IRouteEvents GetRoutesFor(IAggregate aggregate) {
            var mbRoute = routes.Get(aggregate.GetType());

            var route = mbRoute.GetOrElse(() => {// Note: performs side effect on the routes table
                var innerRoute = BuildRoutesFor(aggregate);
                routes[aggregate.GetType()] = innerRoute;
                return innerRoute;
            });

            return new RouteEventsAdapter(route, aggregate);
        }

        private SharedConventionEventRouter BuildRoutesFor(IAggregate aggregate) {
            return new SharedConventionEventRouter(throwOnApplyNotFound: true, aggregateType: aggregate.GetType());
        }
    }
}
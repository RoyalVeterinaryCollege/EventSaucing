using System;
using System.Reflection;
using EventSaucing.NEventStore;
using NEventStore.Domain;
using NEventStore.Domain.Persistence;
using Scalesque;

namespace EventSaucing.Aggregates {
    /// <summary>
    /// Copied from class of same name in NEventstore tests.  Note, doesn't make use of the momento at all.
    /// </summary>
    public class AggregateFactory : IConstructAggregates {
        readonly ISharedEventApplicationRoutes _router;

        public AggregateFactory(ISharedEventApplicationRoutes router) {
            _router = router;
        }

        /// <summary>
        /// Instantiates the aggregate and sets its event router
        /// </summary>
        /// <param name="type"></param>
        /// <param name="id"></param>
        /// <param name="snapshot"></param>
        /// <returns></returns>
        public IAggregate Build(Type type, Guid id, IMemento snapshot) {
            ConstructorInfo constructor = type.GetConstructor(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, binder: null, types: new[] { typeof(Guid) }, modifiers: null);
            var aggregate = constructor.Invoke(new object[] { id }) as IAggregate;
            var setEventRouter = type.GetProperty("RegisteredRoutes", BindingFlags.Instance | BindingFlags.NonPublic);
            if(setEventRouter == null)
                throw new ArgumentNullException("can't find the RegisteredRoutes property on a type called {0}".format(type.Name));
            var eventRouter = _router.GetRoutesFor(aggregate);
            setEventRouter.SetMethod.Invoke(aggregate,new object[] { eventRouter });
            return aggregate;
        }
    }
}